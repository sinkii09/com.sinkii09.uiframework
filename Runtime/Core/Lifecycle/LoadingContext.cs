using Cysharp.Threading.Tasks;
using System;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public sealed class LoadingContext : ILoadingContext
    {
        public string TargetScene { get; private set; }
        public Func<CancellationToken, UniTask> OnLoaded { get; private set; }

        public void Set(string sceneName, Func<CancellationToken, UniTask> onLoaded = null)
        {
            TargetScene = sceneName;
            OnLoaded = onLoaded;
        }

        public void Reset()
        {
            TargetScene = null;
            OnLoaded = null;
        }
    }
}
