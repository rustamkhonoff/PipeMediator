// -------------------------------------------------------------------
// Author: Shokhrukhkhon Rustamkhonov
// Date: 19.11.2025
// -------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using MessagePipe;

namespace PipeMediator
{
    public class RequestHandlerInfo
    {
        public Type RequestType;
        public Type ResponseType;
        public Type HandlerInterface;
        public Type HandlerImplementation;
    }

    public class NotificationHandlerInfo
    {
        public Type NotificationType;
        public Type HandlerInterface;
        public readonly HashSet<Type> HandlerImplementations = new();
    }

    public interface IUMediatrHandlersCollection
    {
        List<RequestHandlerInfo> Requests { get; }
        List<NotificationHandlerInfo> Notifications { get; }
    }

    public class UMediatrHandlersCollection : IUMediatrHandlersCollection
    {
        public List<RequestHandlerInfo> Requests { get; }
        public List<NotificationHandlerInfo> Notifications { get; }

        public UMediatrHandlersCollection(List<RequestHandlerInfo> req, List<NotificationHandlerInfo> notification)
        {
            Requests = req;
            Notifications = notification;
        }
    }

    public static class HandlersScanner
    {
        public static IUMediatrHandlersCollection ScanMessagePipeHandlers(params Assembly[] assemblies)
        {
            List<RequestHandlerInfo> requestList = new();
            List<NotificationHandlerInfo> notificationList = new();

            Dictionary<Type, NotificationHandlerInfo> notificationHandlerInfos = new();

            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (!type.IsClass || type.IsAbstract) continue;

                    foreach (Type i in type.GetInterfaces())
                    {
                        if (!i.IsGenericType) continue;

                        Type genericDef = i.GetGenericTypeDefinition();

                        if (genericDef == typeof(IAsyncRequestHandler<,>))
                        {
                            Type[] args = i.GetGenericArguments();
                            Type requestType = args[0];
                            Type responseType = args[1];

                            Type handlerInterface = typeof(IAsyncRequestHandler<,>).MakeGenericType(requestType, responseType);

                            requestList.Add(new RequestHandlerInfo()
                            {
                                RequestType = requestType,
                                ResponseType = responseType,
                                HandlerInterface = handlerInterface,
                                HandlerImplementation = type
                            });
                        }

                        if (genericDef == typeof(IAsyncMessageHandler<>))
                        {
                            Type notificationType = i.GetGenericArguments()[0];

                            Type handlerInterface = typeof(IAsyncMessageHandler<>).MakeGenericType(notificationType);

                            if (!notificationHandlerInfos.TryGetValue(handlerInterface, out NotificationHandlerInfo info))
                            {
                                info = new NotificationHandlerInfo()
                                {
                                    NotificationType = notificationType,
                                    HandlerInterface = handlerInterface
                                };
                                notificationHandlerInfos[handlerInterface] = info;
                            }

                            info.HandlerImplementations.Add(type);
                        }
                    }
                }
            }

            notificationList.AddRange(notificationHandlerInfos.Values);

            return new UMediatrHandlersCollection(requestList, notificationList);
        }
    }
}