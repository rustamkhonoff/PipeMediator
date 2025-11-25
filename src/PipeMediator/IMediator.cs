// -------------------------------------------------------------------
// Author: Shokhrukhkhon Rustamkhonov
// Date: 25.11.2025
// Description:
// -------------------------------------------------------------------

using System.Threading;
using Cysharp.Threading.Tasks;
using MessagePipe;

namespace PipeMediator
{
    public interface IMediator
    {
        UniTask<T> Send<T>(IRequest<T> request, CancellationToken ct = default);
        UniTask Send(IRequest request, CancellationToken ct = default);
        UniTask Publish(INotification notification, CancellationToken ct = default, AsyncPublishStrategy publishStrategy = AsyncPublishStrategy.Parallel, params IAsyncMessageHandlerFilter[] filters);
    }

    public interface IMediatorServiceResolver
    {
        T Resolve<T>();
    }
}