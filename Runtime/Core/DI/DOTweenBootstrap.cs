using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Initializes DOTween once, before any scene loads, so tweens are safe to create in Awake().
    internal static class DOTweenBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init() =>
            DOTween.Init(recycleAllByDefault: false, useSafeMode: true, logBehaviour: LogBehaviour.ErrorsOnly);
    }
}
