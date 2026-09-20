using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Loads a PREFAB and returns a component off it. The `where T : Component` constraint IS the
    // contract: the key names a prefab, and T must be a component on that prefab. Every
    // implementation therefore loads a GameObject and calls GetComponent<T> — the generic parameter
    // selects a component, it does not select the asset type.
    //
    // Anything that is not a component — a ScriptableObject definition, a Sprite, a TextAsset, an
    // AudioClip — is inexpressible here BY DESIGN and belongs to IAssetLoader. The same loader
    // object implements both interfaces, so a key keeps one lifetime and one release path.
    //
    // Contract: implementations must release any partially-acquired resource (e.g. an Addressables
    // handle) before LoadAsync throws or is cancelled. Callers (UIViewFactory) rely on this to avoid
    // leaking on load failure — they only call UnloadAsync for keys that reached a successful LoadAsync.
    public interface IUILoader
    {
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component;
        UniTask UnloadAsync(string key, CancellationToken ct = default);
    }
}
