using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IBackButtonHandler
    {
        int Priority { get; }
        UniTask HandleBackAsync(CancellationToken ct = default);
    }
}
