using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public interface IUILoader
    {
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : Component;
        UniTask UnloadAsync(string key, CancellationToken ct = default);
    }
}
