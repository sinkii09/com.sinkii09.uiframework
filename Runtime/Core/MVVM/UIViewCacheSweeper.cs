using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Periodically asks UIViewFactory to evict cached views that have gone unused.
    //
    // Without this, UIViewFactory._cache only ever grows: NavigationStack.PopAsync hides a view
    // and drops it from the stack, but nothing destroys it, so every view a player opens holds
    // its GameObject and its loader handle (Addressables ref-count) until the LifetimeScope is
    // destroyed. Two shipped commercial games solved this the same way — a timed grace period
    // plus an explicit resident set — which is the shape implemented here.
    //
    // Registered by UIFrameworkLifetimeScope ONLY when ViewCacheGraceSeconds > 0, so the feature
    // is off by default and an upgrading project behaves exactly as before. Nothing injects this
    // type, so conditional registration is safe (unlike UIViewPolicyResolver — see its comment).
    //
    // Uses a UniTask loop rather than ITickable/MonoBehaviour: the framework has no tick
    // infrastructure, and per-frame ticking to service a multi-second check is waste. Mirrors
    // TransitionOverlayView's UniTask.Delay(DelayType.UnscaledDeltaTime) pattern.
    public sealed class UIViewCacheSweeper : IAsyncStartable, IDisposable
    {
        private readonly UIViewFactory _factory;
        private readonly INavigationStack _stack;
        private readonly UIViewPolicyResolver _policies;
        private readonly float _graceSeconds;
        private readonly float _intervalSeconds;

        private readonly CancellationTokenSource _cts = new();

        // Concrete UIViewFactory, not IUIViewFactory: SweepAsync is internal, deliberately kept
        // off the public interface. Same precedent as GameLifecycleManager taking the concrete
        // UINavigator to reach its internal ChangeStateAsync.
        public UIViewCacheSweeper(UIViewFactory factory, INavigationStack stack,
                                  UIViewPolicyResolver policies, UIFrameworkConfig config)
        {
            _factory = factory;
            _stack = stack;
            _policies = policies;
            _graceSeconds = config != null ? Mathf.Max(0f, config.ViewCacheGraceSeconds) : 0f;
            // Clamped, not trusted: a config created in code bypasses the [Min] Inspector guard,
            // and a 0 or negative interval would sweep every frame.
            _intervalSeconds = config != null ? Mathf.Max(0.1f, config.ViewCacheSweepIntervalSeconds) : 5f;
        }

        public async UniTask StartAsync(CancellationToken startCt)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(startCt, _cts.Token);
            var ct = linked.Token;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_intervalSeconds),
                                        DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, ct);

                    await _factory.SweepAsync(_stack?.All, _graceSeconds, _policies.IsResident, ct);
                }
                catch (OperationCanceledException)
                {
                    return; // shutting down
                }
                catch (Exception ex)
                {
                    // One bad sweep must not kill the loop for the rest of the session — the next
                    // interval retries. Logged rather than swallowed so it is not silent.
                    Debug.LogException(ex);
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
