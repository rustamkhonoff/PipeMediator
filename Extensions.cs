// -------------------------------------------------------------------
// Author: Shokhrukhkhon Rustamkhonov
// Date: 25.11.2025
// Description:
// -------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MessagePipe;
using UnityEngine;
using VContainer;
using ContainerBuilderExtensions = MessagePipe.ContainerBuilderExtensions;

namespace PipeMediator
{
    public static class Extensions
    {
        public class Configuration
        {
            public MessagePipeOptions Options { get; }
            public List<Type> MessageHandlerFilters { get; set; } = new();
            public List<Type> RequestHandlerFilters { get; set; } = new();
            public HashSet<Assembly> AssembliesToScan { get; set; } = new();


            public void UseGlobalNotificationPipeline(params Type[] types)
            {
                foreach (Type type in types)
                {
                    if (typeof(INotificationPipeline).IsAssignableFrom(type))
                        MessageHandlerFilters.Add(type);
                    else
                        Debug.LogException(new ArrayTypeMismatchException($"{type} is not INotificationPipeline"));
                }
            }

            public void UseGlobalRequestPipeline(params Type[] types)
            {
                foreach (Type type in types)
                {
                    if (typeof(IRequestPipeline).IsAssignableFrom(type))
                        RequestHandlerFilters.Add(type);
                    else
                        Debug.LogException(new ArrayTypeMismatchException($"{type} is not IRequestPipeline"));
                }
            }

            public void AddAssembliesFromTypes(params Type[] types)
            {
                foreach (Type type in types)
                    AddAssemblyFromType(type);
            }

            public void AddAssemblyFromType<T>()
            {
                AddAssemblyFromType(typeof(T));
            }

            public void AddAssemblyFromType(Type type)
            {
                AssembliesToScan.Add(type.Assembly);
            }

            public Configuration(MessagePipeOptions options)
            {
                Options = options;
            }
        }

        public static void AddMediatorPipe(this IContainerBuilder builder, MessagePipeOptions options, Action<Configuration> configure)
        {
            Configuration configuration = new(options);
            configure?.Invoke(configuration);
            builder.AddMediatorPipe(configuration);
        }

        public static void AddMediatorPipe(this IContainerBuilder builder, Configuration c)
        {
            builder.AddMediatorPipe(
                c.Options,
                c.AssembliesToScan.ToArray(),
                c.MessageHandlerFilters.ToArray(),
                c.RequestHandlerFilters.ToArray()
            );
        }

        public static void RegisterNotificationPipeline<T>(this IContainerBuilder builder) where T : class, INotificationPipeline, IAsyncMessageHandlerFilter
        {
            builder.RegisterAsyncMessageHandlerFilter<T>();
        }

        public static void RegisterRequestPipeline<T>(this IContainerBuilder builder) where T : class, IRequestPipeline, IAsyncRequestHandlerFilter
        {
            builder.RegisterAsyncRequestHandlerFilter<T>();
        }

        public static void AddMediatorPipe(this IContainerBuilder builder, MessagePipeOptions options, Assembly[] assemblies, Type[] messageHandlerFilters = null, Type[] requestHandlerFilters = null)
        {
            IUMediatrHandlersCollection scan = HandlersScanner.ScanMessagePipeHandlers(assemblies);

            builder.Register<IMediator, Mediator>(Lifetime.Singleton);

            foreach (RequestHandlerInfo info in scan.Requests)
            {
                MethodInfo originalMethod = typeof(ContainerBuilderExtensions)
                    .GetMethod(nameof(ContainerBuilderExtensions.RegisterAsyncRequestHandler));
                MethodInfo genericMethod = originalMethod!.MakeGenericMethod(info.RequestType, info.ResponseType, info.HandlerImplementation);
                genericMethod.Invoke(null, new object[] { builder, options });
            }

            foreach (NotificationHandlerInfo info in scan.Notifications)
            {
                MethodInfo originalMethod = typeof(ContainerBuilderExtensions)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == nameof(ContainerBuilderExtensions.RegisterMessageBroker))
                    .FirstOrDefault(m => m.GetGenericArguments().Length == 1);

                MethodInfo genericMethod = originalMethod!.MakeGenericMethod(info.NotificationType);
                genericMethod.Invoke(null, new object[] { builder, options });

                foreach (Type infoHandlerImplementation in info.HandlerImplementations)
                {
                    Type handlerInterfaceType = typeof(IAsyncMessageHandler<>).MakeGenericType(info.NotificationType);
                    builder.Register(handlerInterfaceType, infoHandlerImplementation, Lifetime.Transient);
                }
            }

            if (messageHandlerFilters is { Length: > 0 })
            {
                foreach (Type filter in messageHandlerFilters)
                    if (!builder.Exists(filter))
                        builder.Register(filter, Lifetime.Transient);

                foreach (NotificationHandlerInfo notificationHandlerInfo in scan.Notifications)
                    options.AddAsyncGlobalMessageFilters(notificationHandlerInfo.NotificationType, messageHandlerFilters);
            }

            if (requestHandlerFilters is { Length: > 0 })
            {
                foreach (Type filter in requestHandlerFilters)
                    if (!builder.Exists(filter))
                        builder.Register(filter, Lifetime.Transient);

                foreach (RequestHandlerInfo notificationHandlerInfo in scan.Requests)
                    options.AddAsyncGlobalRequestFilters(notificationHandlerInfo.RequestType, notificationHandlerInfo.ResponseType, requestHandlerFilters);
            }
        }

        public static void AddAsyncGlobalMessageFilters(this MessagePipeOptions options, Type messageType, params Type[] filterTypes)
        {
            if (filterTypes == null || filterTypes.Length == 0)
                return;

            MethodInfo method = typeof(MessagePipeOptions)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(m =>
                    m.Name == nameof(MessagePipeOptions.AddGlobalAsyncMessageHandlerFilter) &&
                    m.IsGenericMethod &&
                    m.GetParameters().Length == 1);

            foreach (Type filter in filterTypes)
            {
                if (!typeof(IAsyncMessageHandlerFilter).IsAssignableFrom(filter))
                    throw new InvalidOperationException($"{filter.Name} must implement {nameof(IMessageHandlerFilter)}");

                if (!filter.IsGenericTypeDefinition)
                    throw new InvalidOperationException($"{filter.Name} must be open generic");

                Type closedFilter = filter.MakeGenericType(messageType);
                MethodInfo closedMethod = method.MakeGenericMethod(closedFilter);
                closedMethod.Invoke(options, new object[] { 0 });
            }
        }

        public static void AddAsyncGlobalRequestFilters(this MessagePipeOptions options, Type requestType, Type responseType, params Type[] filterTypes)
        {
            if (filterTypes == null || filterTypes.Length == 0)
                return;

            MethodInfo method = typeof(MessagePipeOptions)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(m =>
                    m.Name == nameof(MessagePipeOptions.AddGlobalAsyncRequestHandlerFilter) &&
                    m.IsGenericMethod &&
                    m.GetParameters().Length == 1);

            foreach (Type filter in filterTypes)
            {
                if (!filter.IsGenericTypeDefinition)
                    throw new InvalidOperationException($"{filter.Name} must be open generic");

                if (!typeof(IAsyncRequestHandlerFilter).IsAssignableFrom(filter))
                    throw new InvalidOperationException($"{filter.Name} must implement {nameof(IAsyncRequestHandlerFilter)}");

                Type closedFilter = filter.MakeGenericType(requestType, responseType);
                MethodInfo closedMethod = method.MakeGenericMethod(closedFilter);
                closedMethod.Invoke(options, new object[] { 0 });
            }
        }
    }
}