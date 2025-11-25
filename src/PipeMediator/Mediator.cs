// -------------------------------------------------------------------
// Author: Shokhrukhkhon Rustamkhonov
// Date: 25.11.2025
// Description:
// -------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;
using VContainer;

namespace PipeMediator
{
    public class Mediator : IMediator
    {
        private readonly IObjectResolver m_resolver;
        private readonly Dictionary<Type, INotificationHandlerWrapper> m_notificationHandlerWrappersCache = new();
        private readonly Dictionary<Type, IRequestHandlerWrapperCore> m_requestHandlerWrappersCache = new();


        public Mediator(IObjectResolver resolver)
        {
            m_resolver = resolver;
        }

        public UniTask<T> Send<T>(IRequest<T> request, CancellationToken ct = default)
        {
            Type responseType = typeof(T);
            Type requestType = request.GetType();
            Type handlerType = typeof(IAsyncRequestHandler<,>).MakeGenericType(requestType, responseType);

            if (m_requestHandlerWrappersCache.TryGetValue(handlerType, out IRequestHandlerWrapperCore wrapper) && wrapper is IRequestHandlerWrapper<T> concreteWrapper)
                return concreteWrapper.Invoke(request, ct);

            concreteWrapper = (IRequestHandlerWrapper<T>)Activator.CreateInstance(
                typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, responseType),
                m_resolver
            );
            m_requestHandlerWrappersCache[handlerType] = concreteWrapper;

            return concreteWrapper.Invoke(request, ct);
        }

        public UniTask Send(IRequest request, CancellationToken ct = default)
        {
            Type responseType = typeof(Unit);
            Type requestType = request.GetType();
            Type handlerType = typeof(IAsyncRequestHandler<,>).MakeGenericType(requestType, responseType);

            if (m_requestHandlerWrappersCache.TryGetValue(handlerType, out IRequestHandlerWrapperCore wrapper) && wrapper is IRequestHandlerWrapper concreteWrapper)
                return concreteWrapper.Invoke(request, ct);

            concreteWrapper = (IRequestHandlerWrapper)Activator.CreateInstance(
                typeof(RequestHandlerWrapperCore<>).MakeGenericType(requestType),
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

        #region Notification Wrappers

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

        #endregion

        #region Request Wrappers

        private interface IRequestHandlerWrapperCore { }

        private interface IRequestHandlerWrapper<TResponse> : IRequestHandlerWrapperCore
        {
            UniTask<TResponse> Invoke(IRequest<TResponse> request, CancellationToken ct);
        }

        private interface IRequestHandlerWrapper : IRequestHandlerWrapperCore
        {
            UniTask Invoke(IRequest request, CancellationToken ct);
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

        private class RequestHandlerWrapperCore<TRequest> : IRequestHandlerWrapper
            where TRequest : IRequest
        {
            private readonly IObjectResolver m_objectResolver;

            public RequestHandlerWrapperCore(IObjectResolver objectResolver)
            {
                m_objectResolver = objectResolver;
            }

            public UniTask Invoke(IRequest request, CancellationToken ct)
            {
                IAsyncRequestHandler<TRequest, Unit> handler = m_objectResolver.Resolve<IAsyncRequestHandler<TRequest, Unit>>();

                return handler.InvokeAsync((TRequest)request, ct);
            }
        }

        #endregion
    }
}