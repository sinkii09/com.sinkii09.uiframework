using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUIView
    {
        string ViewId { get; }
        bool IsVisible { get; }

        UniTask InitializeAsync(CancellationToken ct = default);
        UniTask ShowAsync(CancellationToken ct = default);
        UniTask HideAsync(CancellationToken ct = default);
    }
}
