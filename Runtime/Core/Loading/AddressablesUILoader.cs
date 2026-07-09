using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Sinkii09.UIFramework
{
    // Addressables loader — only compiled when the Addressables package is installed.
    // Handles are cached per key and released explicitly via UnloadAsync.
    // Do NOT release handles on view return — release only on application quit or explicit eviction.
    public class AddressablesUILoader : IUILoader
    {
        private readonly Dictionary<string, AsyncOperationHandle> _handles = new();

        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component
        {
            // C3: validate cached handle before returning — stale/released handles yield undefined results.
            if (_handles.TryGetValue(key, out var cached))
            {
                if (cached.IsValid() && cached.Status == AsyncOperationStatus.Succeeded)
                {
                    var cachedComponent = ((GameObject)cached.Result).GetComponent<T>();
                    if (cachedComponent == null)
                        throw new System.InvalidOperationException(
                            $"[AddressablesUILoader] Cached prefab '{key}' has no {typeof(T).Name} component.");
                    return cachedComponent;
                }
                // Stale entry — release the handle before evicting to avoid leaking the Addressables ref-count.
                if (cached.IsValid())
                    Addressables.Release(cached);
                _handles.Remove(key);
            }

            var handle = Addressables.LoadAssetAsync<GameObject>(key);
            try
            {
                await handle.ToUniTask(cancellationToken: ct);
                ct.ThrowIfCancellationRequested();

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new System.InvalidOperationException(
                        $"[AddressablesUILoader] Failed to load '{key}': {handle.OperationException?.Message}");

                var component = handle.Result.GetComponent<T>();
                if (component == null)
                    throw new System.InvalidOperationException(
                        $"[AddressablesUILoader] Prefab '{key}' has no {typeof(T).Name} component.");

                _handles[key] = handle;
                return component;
            }
            catch
            {
                // C2: release the in-flight handle on any failure (cancel, load error, component missing)
                // to prevent VRAM leaks on mobile.
                if (!_handles.ContainsKey(key) && handle.IsValid())
                    Addressables.Release(handle);
                throw;
            }
        }

        // No-op if key not cached — avoids KeyNotFoundException on double-unload.
        public UniTask UnloadAsync(string key, CancellationToken ct = default)
        {
            if (_handles.TryGetValue(key, out var handle))
            {
                Addressables.Release(handle);
                _handles.Remove(key);
            }
            else
            {
                Debug.LogWarning($"[AddressablesUILoader] UnloadAsync: key '{key}' not in cache — skipping.");
            }
            return UniTask.CompletedTask;
        }
    }
}
