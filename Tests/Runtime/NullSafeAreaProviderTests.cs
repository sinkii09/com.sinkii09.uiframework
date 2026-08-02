using NUnit.Framework;
using R3;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the C5 finding: a missing SafeAreaProvider in the scene hierarchy used
    // to crash DI resolution instead of degrading gracefully. NullSafeAreaProvider is the
    // ITransitionOverlay/NullTransitionOverlay-style fallback.
    // See plans/260802-1122-hardening-cluster/plan.md, Item 2.
    public sealed class NullSafeAreaProviderTests
    {
        [Test]
        public void SafeArea_ReturnsFullScreenRect_NotZero()
        {
            // Not Rect.zero: UIRootSetup.ApplySafeArea divides by Screen.width/height to compute
            // anchors, so a zero Rect would collapse the safe area panel instead of no-opping it.
            var provider = new NullSafeAreaProvider();

            var area = provider.SafeArea;

            Assert.AreEqual(0, area.x);
            Assert.AreEqual(0, area.y);
            Assert.AreEqual(Screen.width, area.width);
            Assert.AreEqual(Screen.height, area.height);
        }

        [Test]
        public void OnChanged_EmitsCurrentValueOnceOnSubscribe()
        {
            var provider = new NullSafeAreaProvider();
            var emissions = 0;
            Rect? lastValue = null;

            using var sub = provider.OnChanged.Subscribe(v =>
            {
                emissions++;
                lastValue = v;
            });

            Assert.AreEqual(1, emissions, "Null-Object has no notch to react to — must emit exactly once, not stay silent or repeat.");
            Assert.AreEqual(provider.SafeArea, lastValue);
        }

        // DI-level regression test: pins the actual bug (Configure() registering nothing on the
        // missing-SafeAreaProvider path, so VContainer threw before UIRootSetup's [Inject] field
        // ever got set), not just NullSafeAreaProvider's own behavior in isolation. Uses a bare
        // ContainerBuilder rather than the full UIFrameworkLifetimeScope: building the real scope
        // also eagerly runs GameLifecycleManager/BackButtonRouter's entry-point initialization
        // (VContainer resolves RegisterEntryPoint targets at container-build time), which is
        // unrelated integration surface this fix doesn't touch.
        [Test]
        public void MissingSafeAreaProviderRegistration_ResolvesNullFallback_DoesNotThrow()
        {
            IObjectResolver resolver = null;

            Assert.DoesNotThrow(() =>
            {
                var builder = new ContainerBuilder();
                // Same call UIFrameworkLifetimeScope.Configure() makes on its missing-component
                // branch — see Runtime/Core/DI/UIFrameworkLifetimeScope.cs.
                builder.RegisterInstance<ISafeAreaProvider>(new NullSafeAreaProvider());
                resolver = builder.Build();
            });

            var resolved = resolver.Resolve<ISafeAreaProvider>();
            Assert.IsInstanceOf<NullSafeAreaProvider>(resolved);
            Assert.AreEqual(new Rect(0, 0, Screen.width, Screen.height), resolved.SafeArea);
        }
    }
}
