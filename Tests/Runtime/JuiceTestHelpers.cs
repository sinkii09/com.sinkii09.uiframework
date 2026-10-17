using System;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    // Realtime waits for the juice tests — every juice tween can run in unscaled time, and one test
    // deliberately sets Time.timeScale = 0, so scaled waits would hang or prove nothing.
    internal static class JuiceTestHelpers
    {
        internal static UniTask WaitRealtime(float seconds) =>
            UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.Realtime);

        internal static async UniTask WaitUntil(Func<bool> condition, float timeoutSeconds = 3f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail($"Condition not met within {timeoutSeconds}s");
                await UniTask.Yield();
            }
        }
    }
}
