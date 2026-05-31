using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IViewState
    {
        UniTask OnEnterAsync(CancellationToken ct = default);
        UniTask OnExitAsync(CancellationToken ct = default);
    }
}
