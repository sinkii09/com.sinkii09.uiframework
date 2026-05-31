using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public interface IUILoader
    {
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component;
        // Implementation must ref-count Addressables handles per key.
        // Calling UnloadAsync while another view holds the same key releases the shared handle.
        UniTask UnloadAsync(string key, CancellationToken ct = default);
    }
}
