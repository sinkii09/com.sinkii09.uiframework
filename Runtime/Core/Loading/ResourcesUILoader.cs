using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Default loader — no additional packages required.
    // Key = path relative to any Resources folder (e.g. "UI/MainMenuView" loads Resources/UI/MainMenuView.prefab).
    //
    // Implements BOTH loader interfaces deliberately. They are two questions about one backend, and
    // splitting them across two objects would give the same key two independent lifetimes. Under
    // Resources that is merely redundant; under Addressables it would be two ref-count ledgers for
    // one handle, which is the bug this shape exists to avoid.
    public class ResourcesUILoader : IUILoader, IAssetLoader
    {
        // IUILoader: the key names a PREFAB and the caller wants a component off it.
        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component
        {
            var prefab = await LoadAssetAsync<GameObject>(key, ct);

            var component = prefab.GetComponent<T>();
            if (component == null)
                throw new System.InvalidOperationException(
                    $"[ResourcesUILoader] Prefab at Resources/{key} has no {typeof(T).Name} component.");

            return component;
        }

        // IAssetLoader: the key names the asset itself — ScriptableObject, Sprite, TextAsset, AudioClip.
        public async UniTask<T> LoadAssetAsync<T>(string key, CancellationToken ct = default)
            where T : Object
        {
            var request = Resources.LoadAsync<T>(key);
            await request.ToUniTask(cancellationToken: ct);
            ct.ThrowIfCancellationRequested();

            // Resources.LoadAsync<T> also yields null when the path EXISTS but holds another type, so
            // the message names the type and not only the path — otherwise a wrong-type key reads as
            // a missing file and sends the reader looking in the wrong place.
            if (request.asset == null)
                throw new System.InvalidOperationException(
                    $"[ResourcesUILoader] No {typeof(T).Name} at Resources/{key}. " +
                    $"Ensure the asset exists in a Resources folder and is a {typeof(T).Name}.");

            return (T)request.asset;
        }

        // A no-op, and NOT because Resources reference-counts — it does not. An asset loaded from
        // Resources stays in memory until Resources.UnloadUnusedAssets() runs, which is a
        // whole-heap operation this class has no business triggering per key.
        //
        // Deliberately NOT Resources.UnloadAsset either: it throws for GameObject and Component, and
        // for every other type it destroys the one shared instance out from under every other holder
        // of that key.
        //
        // Consequence worth knowing before shipping content packs on this backend: unloading a pack
        // under Resources frees nothing, while the same call under Addressables frees immediately.
        // If pack memory matters, LoaderMode.Addressables is not optional.
        public UniTask UnloadAsync(string key, CancellationToken ct = default) => UniTask.CompletedTask;
    }
}
