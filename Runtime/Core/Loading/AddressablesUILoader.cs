using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Sinkii09.UIFramework
{
    // Addressables loader. ALWAYS compiled — com.unity.addressables is a hard dependency in this
    // package's package.json and a hard asmdef reference, so it is always present.
    //
    // This used to claim it was "only compiled when the Addressables package is installed", which
    // stopped being true when 9178d58 removed the #if ADDRESSABLES guards on the reasoning that
    // "the define is always set". That define was hand-set in each consuming project's
    // ProjectSettings, never a guarantee the package could rely on, and it has since been deleted
    // outright. What makes this safe is the package dependency, not a define — so do not
    // reintroduce a hand-set symbol here. If Addressables ever needs to become optional, it has to
    // be a versionDefines entry on com.unity.addressables, which Unity sets from actual package
    // presence.
    //
    // The runtime switch between this and ResourcesUILoader is UIFrameworkConfig.LoaderMode.
    // Handles are cached and released explicitly via UnloadAsync.
    // Do NOT release handles on view return — release only on application quit or explicit eviction.
    //
    // Implements BOTH loader interfaces against ONE cache. That sharing is the point: a second
    // loader object for non-prefab assets would keep a second ref-count ledger for the same
    // addresses and release them twice.
    public class AddressablesUILoader : IUILoader, IAssetLoader
    {
        // Keyed by address AND type, not by address alone. One address can legitimately be loaded as
        // more than one type — a sprite atlas entry serves both Texture2D and Sprite, each with its
        // own handle — so an address-only key would either reject the second load or overwrite the
        // first handle and leak it. UnloadAsync releases every type held for an address.
        private readonly Dictionary<(string Address, Type Type), AsyncOperationHandle> _handles = new();

        // Reused by UnloadAsync so a release does not allocate; never held across an await.
        private readonly List<(string Address, Type Type)> _unloadScratch = new();

        // IUILoader: the key names a PREFAB and the caller wants a component off it.
        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component
        {
            var id = (key, typeof(GameObject));

            if (TryServeFromCache<GameObject>(id, out var cachedPrefab))
                return ComponentOrThrow<T>(cachedPrefab, key, "Cached prefab");

            var handle = Addressables.LoadAssetAsync<GameObject>(key);
            try
            {
                var prefab = await AwaitAsset(handle, key, ct);

                // Checked BEFORE the handle is committed, so the catch below still owns it. Moving
                // this after the commit would strand the handle on a prefab missing the component.
                var component = ComponentOrThrow<T>(prefab, key, "Prefab");

                Commit(id, handle);
                return component;
            }
            catch
            {
                // C2: release the in-flight handle on any failure (cancel, load error, component
                // missing) to prevent VRAM leaks on mobile.
                ReleaseIfUnowned(id, handle);
                throw;
            }
        }

        // IAssetLoader: the key names the asset itself — ScriptableObject, Sprite, TextAsset, AudioClip.
        public async UniTask<T> LoadAssetAsync<T>(string key, CancellationToken ct = default)
            where T : UnityEngine.Object
        {
            var id = (key, typeof(T));

            if (TryServeFromCache<T>(id, out var cachedAsset))
                return cachedAsset;

            var handle = Addressables.LoadAssetAsync<T>(key);
            try
            {
                var asset = await AwaitAsset(handle, key, ct);
                return Commit(id, handle) ? asset : (T)_handles[id].Result;
            }
            catch
            {
                ReleaseIfUnowned(id, handle);
                throw;
            }
        }

        // Releases every type held for this address. No-op if none are cached — avoids throwing on a
        // double-unload, which a teardown path can easily do twice.
        public UniTask UnloadAsync(string key, CancellationToken ct = default)
        {
            _unloadScratch.Clear();

            foreach (var pair in _handles)
            {
                if (pair.Key.Address == key)
                    _unloadScratch.Add(pair.Key);
            }

            if (_unloadScratch.Count == 0)
            {
                Debug.LogWarning($"[AddressablesUILoader] UnloadAsync: key '{key}' not in cache — skipping.");
                return UniTask.CompletedTask;
            }

            for (var i = 0; i < _unloadScratch.Count; i++)
            {
                var id = _unloadScratch[i];
                if (_handles[id].IsValid())
                    Addressables.Release(_handles[id]);
                _handles.Remove(id);
            }

            _unloadScratch.Clear();
            return UniTask.CompletedTask;
        }

        // C3: validate the cached handle before returning it — stale or released handles yield
        // undefined results. The cast is safe because the type is part of the cache key.
        private bool TryServeFromCache<T>(in (string Address, Type Type) id, out T asset) where T : UnityEngine.Object
        {
            asset = null;

            if (!_handles.TryGetValue(id, out var cached))
                return false;

            if (cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
            {
                asset = (T)cached.Result;
                return true;
            }

            // Stale entry — release the handle before evicting to avoid leaking the Addressables ref-count.
            if (cached.IsValid())
                Addressables.Release(cached);
            _handles.Remove(id);
            return false;
        }

        // Publishes a freshly loaded handle, unless a concurrent load for the same id got there
        // first. Two calls for one id both miss the cache and both acquire a handle; without this
        // the second assignment would overwrite the first and leak it for the session.
        private bool Commit<T>(in (string Address, Type Type) id, AsyncOperationHandle<T> handle)
        {
            if (_handles.TryGetValue(id, out var owned) && !owned.Equals((AsyncOperationHandle)handle))
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                return false;
            }

            _handles[id] = handle;
            return true;
        }

        private static async UniTask<T> AwaitAsset<T>(AsyncOperationHandle<T> handle, string key,
                                                      CancellationToken ct)
        {
            await handle.ToUniTask(cancellationToken: ct);
            ct.ThrowIfCancellationRequested();

            if (handle.Status != AsyncOperationStatus.Succeeded)
                throw new InvalidOperationException(
                    $"[AddressablesUILoader] Failed to load '{key}': {handle.OperationException?.Message}");

            return handle.Result;
        }

        private static T ComponentOrThrow<T>(GameObject prefab, string key, string subject) where T : Component
        {
            var component = prefab.GetComponent<T>();
            if (component == null)
                throw new InvalidOperationException(
                    $"[AddressablesUILoader] {subject} '{key}' has no {typeof(T).Name} component.");

            return component;
        }

        // Releases our handle unless the cache is holding this exact one. Covers both the plain
        // failure case and the lost-race case, where the cache holds someone else's handle for the
        // same id and ours would otherwise be dropped on the floor still retained.
        private void ReleaseIfUnowned<T>(in (string Address, Type Type) id, AsyncOperationHandle<T> handle)
        {
            if (!handle.IsValid())
                return;

            if (_handles.TryGetValue(id, out var owned) && owned.Equals((AsyncOperationHandle)handle))
                return;

            Addressables.Release(handle);
        }
    }
}
