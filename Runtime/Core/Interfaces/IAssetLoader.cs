using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    // Loads an asset BY ITSELF: a ScriptableObject definition, a Sprite, a TextAsset, an AudioClip.
    //
    // Sibling of IUILoader, not a replacement. IUILoader loads a PREFAB and hands back a component
    // off it (`where T : Component`), which makes loading anything that is not a component
    // inexpressible -- not difficult, the wrong type. Content packs are defined by loading
    // non-component assets under a key prefix, so they need this interface to exist at all.
    //
    // The method is LoadAssetAsync rather than LoadAsync because generic CONSTRAINTS are not part
    // of a method signature in C#: `LoadAsync<T>(string, CancellationToken)` declared on both
    // interfaces is a duplicate member (CS0111) on any class implementing both. The name also
    // matches Addressables.LoadAssetAsync, which does the same job.
    //
    // UnloadAsync repeats IUILoader's signature EXACTLY, on purpose: one implementation then
    // satisfies both interfaces, so a key has one release path instead of two.
    //
    // Contract, same as IUILoader: implementations must release any partially-acquired resource
    // before LoadAssetAsync throws or is cancelled. Callers only call UnloadAsync for keys that
    // reached a successful load.
    public interface IAssetLoader
    {
        UniTask<T> LoadAssetAsync<T>(string key, CancellationToken ct = default)
            where T : UnityEngine.Object;

        UniTask UnloadAsync(string key, CancellationToken ct = default);
    }
}
