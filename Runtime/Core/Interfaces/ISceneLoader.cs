using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine.SceneManagement;

namespace Sinkii09.UIFramework
{
    public interface ISceneLoader
    {
        UniTask LoadAsync(string sceneName, LoadSceneMode mode, CancellationToken ct = default, IProgress<float> progress = null);
        UniTask UnloadAsync(string sceneName, CancellationToken ct = default);
    }
}
