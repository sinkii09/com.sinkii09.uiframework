using Cysharp.Threading.Tasks;
using System;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface ILoadingContext
    {
        string TargetScene { get; }
        // Invoked by LoadingState after scene loads. null = no auto-transition.
        Func<CancellationToken, UniTask> OnLoaded { get; }
        void Set(string sceneName, Func<CancellationToken, UniTask> onLoaded = null);
        // Called by LoadingState after consuming to prevent stale re-use.
        void Reset();
    }
}
