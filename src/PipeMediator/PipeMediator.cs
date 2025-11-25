// -------------------------------------------------------------------
// Author: Shokhrukhkhon Rustamkhonov
// Date: 19.11.2025
// Description:
// -------------------------------------------------------------------

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;

namespace PipeMediator
{
    public readonly struct Unit
    {
        public static readonly Unit Default = new();
    }

    public interface IRequestPipeline { }

    public interface INotificationPipeline { }

    public abstract class RequestPipeline<TRequest, TResponse> : AsyncRequestHandlerFilter<TRequest, TResponse>, IRequestPipeline
    {
        public sealed override UniTask<TResponse> InvokeAsync(TRequest request, CancellationToken cancellationToken, Func<TRequest, CancellationToken, UniTask<TResponse>> next)
        {
            return Handle(request, cancellationToken, next);
        }

        public abstract UniTask<TResponse> Handle(TRequest request, CancellationToken ct, Func<TRequest, CancellationToken, UniTask<TResponse>> next);
    }

    public abstract class NotificationPipeline<TNotification> : AsyncMessageHandlerFilter<TNotification>, INotificationPipeline
    {
        public sealed override UniTask HandleAsync(TNotification notification, CancellationToken cancellationToken, Func<TNotification, CancellationToken, UniTask> next)
        {
            return Handle(notification, cancellationToken, next);
        }

        public abstract UniTask Handle(TNotification notification, CancellationToken cancellationToken, Func<TNotification, CancellationToken, UniTask> next);
    }

    public abstract class RequestHandler<TRequest, TResponse> : IAsyncRequestHandler<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public UniTask<TResponse> InvokeAsync(TRequest request, CancellationToken cancellationToken = new())
        {
            return Handle(request, cancellationToken);
        }

        public abstract UniTask<TResponse> Handle(TRequest request, CancellationToken ct);
    }

    public abstract class RequestHandler<TRequest> : IAsyncRequestHandler<TRequest, Unit>
        where TRequest : IRequest
    {
        public abstract UniTask Handle(TRequest request, CancellationToken ct);

        public async UniTask<Unit> InvokeAsync(TRequest request, CancellationToken cancellationToken = new CancellationToken())
        {
            await Handle(request, cancellationToken);
            return Unit.Default;
        }
    }

    public abstract class NotificationHandler<TNotification> : IAsyncMessageHandler<TNotification>
        where TNotification : INotification
    {
        public UniTask HandleAsync(TNotification message, CancellationToken cancellationToken)
        {
            return Handle(message, cancellationToken);
        }

        public abstract UniTask Handle(TNotification message, CancellationToken ct);
    }

    public interface IRequest<TResponse> { }

    public interface IRequest { }

    public interface INotification { }
}