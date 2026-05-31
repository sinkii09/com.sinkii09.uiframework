using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IBackButtonRouter
    {
        void Register(IBackButtonHandler handler);
        void Unregister(IBackButtonHandler handler);
        UniTask HandleBackAsync(CancellationToken ct = default);
    }
}
