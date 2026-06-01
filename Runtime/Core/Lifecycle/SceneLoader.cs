using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sinkii09.UIFramework
{
    public sealed class SceneLoader : ISceneLoader
    {
        public async UniTask LoadAsync(string sceneName, LoadSceneMode mode,
            CancellationToken ct = default, IProgress<float> progress = null)
        {
            var op = SceneManager.LoadSceneAsync(sceneName, mode);
            if (op == null)
                throw new InvalidOperationException(
                    $"[SceneLoader] Scene '{sceneName}' not found. Ensure it is added to Build Settings.");

            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(op.progress / 0.9f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            progress?.Report(1f);
            op.allowSceneActivation = true;
            // Do not pass ct here — once activation is committed Unity will complete
            // the load regardless, and cancelling the await would orphan the load.
            await op.ToUniTask();
        }

        public async UniTask UnloadAsync(string sceneName, CancellationToken ct = default)
        {
            var op = SceneManager.UnloadSceneAsync(sceneName);
            if (op == null)
            {
                Debug.LogWarning($"[SceneLoader] UnloadAsync: scene '{sceneName}' was not loaded or not found.");
                return;
            }
            await op.WithCancellation(ct);
        }
    }
}
