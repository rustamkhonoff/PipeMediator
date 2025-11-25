// -------------------------------------------------------------------
// Author: Shokhrukhkhon Rustamkhonov
// Date: 19.11.2025
// Description:
// -------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using VContainer;
using ContainerBuilderExtensions = MessagePipe.ContainerBuilderExtensions;

namespace PipeMediator
{
    public interface IMediator
    {
        UniTask<T> Send<T>(IRequest<T> request, CancellationToken ct = default);
        UniTask Publish(INotification notification, CancellationToken ct = default, AsyncPublishStrategy publishStrategy = AsyncPublishStrategy.Parallel, params IAsyncMessageHandlerFilter[] filters);
    }

    public class Mediator : IMediator
    {
        private readonly IObjectResolver m_resolver;
        private readonly Dictionary<Type, INotificationHandlerWrapper> m_notificationHandlerWrappersCache = new();
        private readonly Dictionary<Type, IRequestHandlerWrapper> m_requestHandlerWrappersCache = new();


        public Mediator(IObjectResolver resolver)
        {
            m_resolver = resolver;
        }

        public UniTask<T> Send<T>(IRequest<T> request, CancellationToken ct = default)
        {
            Type responseType = typeof(T);
            Type requestType = request.GetType();
            Type handlerType = typeof(IAsyncRequestHandler<,>).MakeGenericType(requestType, responseType);

            if (m_requestHandlerWrappersCache.TryGetValue(handlerType, out IRequestHandlerWrapper wrapper) && wrapper is IRequestHandlerWrapper<T> concreteWrapper)
                return concreteWrapper.Invoke(request, ct);

            concreteWrapper = (IRequestHandlerWrapper<T>)Activator.CreateInstance(
                typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, responseType),
                m_resolver
            );
            m_requestHandlerWrappersCache[handlerType] = concreteWrapper;

            return concreteWrapper.Invoke(request, ct);
        }

        public UniTask Publish(INotification notification, CancellationToken ct = default, AsyncPublishStrategy asyncPublishStrategy = AsyncPublishStrategy.Parallel, params IAsyncMessageHandlerFilter[] filters)
        {
            Type notificationType = notification.GetType();

            if (m_notificationHandlerWrappersCache.TryGetValue(notificationType, out INotificationHandlerWrapper wrapper))
                return wrapper.Invoke(notification, ct, asyncPublishStrategy, filters);

            wrapper = (INotificationHandlerWrapper)Activator.CreateInstance(
                typeof(NotificationHandlerWrapper<>).MakeGenericType(notification.GetType()),
                m_resolver
            );
            m_notificationHandlerWrappersCache[notificationType] = wrapper;

            return wrapper.Invoke(notification, ct, asyncPublishStrategy, filters);
        }

        private interface INotificationHandlerWrapper
        {
            UniTask Invoke(INotification notification, CancellationToken ct, AsyncPublishStrategy strategy, params IAsyncMessageHandlerFilter[] filters);
        }

        private class NotificationHandlerWrapper<T> : INotificationHandlerWrapper
            where T : INotification
        {
            private readonly IObjectResolver m_resolver;

            public NotificationHandlerWrapper(IObjectResolver resolver)
            {
                m_resolver = resolver;
            }

            public async UniTask Invoke(INotification notification, CancellationToken ct, AsyncPublishStrategy strategy, params IAsyncMessageHandlerFilter[] filters)
            {
                IAsyncPublisher<T> publisher = m_resolver.Resolve<IAsyncPublisher<T>>();
                IAsyncSubscriber<T> subscriber = m_resolver.Resolve<IAsyncSubscriber<T>>();
                IEnumerable<IAsyncMessageHandler<T>> handlers = m_resolver.Resolve<IEnumerable<IAsyncMessageHandler<T>>>();

                DisposableBagBuilder bag = DisposableBag.CreateBuilder();

                AsyncMessageHandlerFilter<T>[] realFilters = filters.Length == 0
                    ? Array.Empty<AsyncMessageHandlerFilter<T>>()
                    : filters.OfType<AsyncMessageHandlerFilter<T>>().ToArray();

                foreach (IAsyncMessageHandler<T> asyncMessageHandler in handlers)
                    subscriber.Subscribe(asyncMessageHandler, realFilters).AddTo(bag);

                IDisposable disposable = bag.Build();

                await publisher.PublishAsync((T)notification, strategy, ct);
                disposable.Dispose();
            }
        }

        private interface IRequestHandlerWrapper { }

        private interface IRequestHandlerWrapper<TResponse> : IRequestHandlerWrapper
        {
            UniTask<TResponse> Invoke(IRequest<TResponse> request, CancellationToken ct);
        }

        private class RequestHandlerWrapper<TRequest, TResponse> : IRequestHandlerWrapper<TResponse>
            where TRequest : IRequest<TResponse>
        {
            private readonly IObjectResolver m_objectResolver;

            public RequestHandlerWrapper(IObjectResolver objectResolver)
            {
                m_objectResolver = objectResolver;
            }

            public UniTask<TResponse> Invoke(IRequest<TResponse> request, CancellationToken ct)
            {
                IAsyncRequestHandler<TRequest, TResponse> handler = m_objectResolver.Resolve<IAsyncRequestHandler<TRequest, TResponse>>();

                return handler.InvokeAsync((TRequest)request, ct);
            }
        }
    }

    public interface IRequest<TResponse> { }

    public interface INotification { }


    public static class Extensions
    {
        public class Configuration
        {
            public MessagePipeOptions Options { get; }
            public List<Type> MessageHandlerFilters { get; set; } = new();
            public List<Type> RequestHandlerFilters { get; set; } = new();
            public HashSet<Assembly> AssembliesToScan { get; set; } = new();

            public void UseGlobalMessageHandlerFilters(params Type[] types)
            {
                MessageHandlerFilters.AddRange(types);
            }

            public void UseGlobalRequestHandlerFilters(params Type[] types)
            {
                RequestHandlerFilters.AddRange(types);
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
                    builder.Register(filter, Lifetime.Transient);

                foreach (NotificationHandlerInfo notificationHandlerInfo in scan.Notifications)
                    options.AddAsyncGlobalMessageFilters(notificationHandlerInfo.NotificationType, messageHandlerFilters);
            }

            if (requestHandlerFilters is { Length: > 0 })
            {
                foreach (Type filter in requestHandlerFilters)
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