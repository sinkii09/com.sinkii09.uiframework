using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Default loader — no additional packages required.
    // Key = path relative to any Resources folder (e.g. "UI/MainMenuView" loads Resources/UI/MainMenuView.prefab).
    public class ResourcesUILoader : IUILoader
    {
        public async UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component
        {
            var request = Resources.LoadAsync<GameObject>(key);
            await request.ToUniTask(cancellationToken: ct);
            ct.ThrowIfCancellationRequested();

            if (request.asset == null)
                throw new System.InvalidOperationException(
                    $"[ResourcesUILoader] Prefab not found at Resources/{key}. " +
                    $"Ensure the prefab exists in a Resources folder.");

            var prefab = (GameObject)request.asset;
            var component = prefab.GetComponent<T>();
            if (component == null)
                throw new System.InvalidOperationException(
                    $"[ResourcesUILoader] Prefab at Resources/{key} has no {typeof(T).Name} component.");

            return component;
        }

        // Resources prefabs are managed by Unity's reference counting; no explicit unload needed.
        public UniTask UnloadAsync(string key, CancellationToken ct = default) => UniTask.CompletedTask;
    }
}
