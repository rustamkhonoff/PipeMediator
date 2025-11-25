// -------------------------------------------------------------------
// Author: Shokhrukhkhon Rustamkhonov
// Date: 19.11.2025
// Description:
// -------------------------------------------------------------------

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

    public abstract class RequestPipeline<T1, T2> : AsyncRequestHandlerFilter<T1, T2>, IRequestPipeline { }

    public abstract class NotificationPipeline<T1> : AsyncMessageHandlerFilter<T1>, INotificationPipeline { }

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