using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IBackButtonRouter
    {
        void Register(IBackButtonHandler handler);
        void Unregister(IBackButtonHandler handler);
        // Calls handlers in descending Priority order; walks to next if the current handler
        // does not consume the event (returns without cancelling the chain).
        UniTask HandleBackAsync(CancellationToken ct = default);
    }
}
