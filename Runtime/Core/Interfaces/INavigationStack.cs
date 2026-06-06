using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface INavigationStack
    {
        int Count { get; }
        IReadOnlyList<IUIView> All { get; }

        UniTask PushAsync(IUIView view, CancellationToken ct = default);
        UniTask<IUIView> PopAsync(CancellationToken ct = default);
        IUIView Peek();
        UniTask ClearAsync(CancellationToken ct = default);
    }
}
