using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Contract: implementations must release any partially-acquired resource (e.g. an Addressables
    // handle) before LoadAsync throws or is cancelled. Callers (UIViewFactory) rely on this to avoid
    // leaking on load failure — they only call UnloadAsync for keys that reached a successful LoadAsync.
    public interface IUILoader
    {
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component;
        UniTask UnloadAsync(string key, CancellationToken ct = default);
    }
}
