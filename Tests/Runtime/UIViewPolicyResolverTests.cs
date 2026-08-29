using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Sinkii09.UIFramework;

namespace Sinkii09.UIFramework.Tests
{
    // Covers UIViewPolicyResolver and the UIViewKeys.For derivation it depends on.
    // Pure logic — no GameObjects, no frames, no container. See the sprint plan
    // (view policy / eviction / backdrop / preload) for the design these back.
    public sealed class UIViewPolicyResolverTests
    {
        private readonly List<UIViewPolicyConfig> _assets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var asset in _assets)
                if (asset != null) UnityEngine.Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        private UIViewPolicyConfig Config(params UIViewPolicyEntry[] entries)
        {
            var asset = ScriptableObject.CreateInstance<UIViewPolicyConfig>();
            asset.Entries = new List<UIViewPolicyEntry>(entries);
            _assets.Add(asset);
            return asset;
        }

        private static UIViewPolicyEntry Entry(string key, bool resident = false,
                                               bool backdrop = false, bool preload = false)
            => new UIViewPolicyEntry
            {
                ViewKey = key,
                Policy = new UIViewPolicy { Resident = resident, NeedsBackdrop = backdrop, PreloadOnBoot = preload }
            };

        // --- UIViewKeys.For ---------------------------------------------------------------

        [Test]
        public void UIViewKeys_NoAttribute_UsesTypeName()
            => Assert.AreEqual(nameof(TestView), UIViewKeys.For(typeof(TestView)));

        [Test]
        public void UIViewKeys_WithAttribute_PrefersAttributeKey()
            => Assert.AreEqual(TestViewWithKey.Key, UIViewKeys.For(typeof(TestViewWithKey)));

        [Test]
        public void UIViewKeys_NullType_Throws()
            => Assert.Throws<ArgumentNullException>(() => UIViewKeys.For(null));

        // --- Null-object behaviour --------------------------------------------------------

        // The resolver is registered unconditionally (VContainer ignores optional-param defaults),
        // so the no-config case is the DEFAULT shipping path, not an edge case.
        [Test]
        public void NoConfig_EveryViewGetsDefaultPolicy()
        {
            var resolver = new UIViewPolicyResolver(null);

            Assert.AreEqual(UIViewPolicy.Default, resolver.Get(typeof(TestView)));
            Assert.IsFalse(resolver.IsResident(typeof(TestView)));
            Assert.IsFalse(resolver.NeedsBackdrop(typeof(TestView)));
        }

        [Test]
        public void NullType_ReturnsDefaultInsteadOfThrowing()
            => Assert.AreEqual(UIViewPolicy.Default, new UIViewPolicyResolver(null).Get(null));

        [Test]
        public void UnknownType_ReturnsDefault()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(nameof(TestView), resident: true)));
            Assert.IsFalse(resolver.IsResident(typeof(SecondTestView)));
        }

        // --- Lookup ------------------------------------------------------------------------

        [Test]
        public void PolicyKeyedByTypeName_ResolvesForViewWithoutAttribute()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(nameof(TestView), resident: true, backdrop: true)));

            Assert.IsTrue(resolver.IsResident(typeof(TestView)));
            Assert.IsTrue(resolver.NeedsBackdrop(typeof(TestView)));
        }

        // A view with [UIViewKey] must be declared by its ATTRIBUTE key, not its class name —
        // this is the pairing that makes the SO's string keys line up with the loader's keys.
        [Test]
        public void PolicyKeyedByAttributeKey_ResolvesForViewWithAttribute()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(TestViewWithKey.Key, resident: true)));
            Assert.IsTrue(resolver.IsResident(typeof(TestViewWithKey)));
        }

        [Test]
        public void PolicyKeyedByClassName_DoesNotResolveForViewWithAttribute()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(nameof(TestViewWithKey), resident: true)));
            Assert.IsFalse(resolver.IsResident(typeof(TestViewWithKey)),
                "A view carrying [UIViewKey] is addressed by its attribute key; its class name must not match.");
        }

        [Test]
        public void BlankAndNullKeys_AreIgnored()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(null, resident: true),
                                                           Entry("   ", resident: true),
                                                           Entry(nameof(TestView), resident: true)));
            Assert.IsTrue(resolver.IsResident(typeof(TestView)));
        }

        [Test]
        public void DuplicateKeys_FirstWinsAndLogsError()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Duplicate policy entry"));

            var resolver = new UIViewPolicyResolver(Config(Entry(nameof(TestView), backdrop: true),
                                                           Entry(nameof(TestView), backdrop: false)));
            Assert.IsTrue(resolver.NeedsBackdrop(typeof(TestView)));
        }

        // --- The derived rule --------------------------------------------------------------

        // Without this, the sweeper evicts exactly what preload just warmed — the interaction
        // bug between the eviction and preload features.
        [Test]
        public void PreloadOnBoot_ImpliesResident()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(nameof(TestView), preload: true)));

            Assert.IsTrue(resolver.IsResident(typeof(TestView)));
            Assert.IsFalse(resolver.Get(typeof(TestView)).Resident,
                "Residency is DERIVED here; the authored Resident flag itself stays false.");
        }

        [Test]
        public void PreloadSet_ReturnsOnlyFlaggedViews()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(nameof(TestView), preload: true)));
            var all = new List<UIViewRegistration>
            {
                new UIViewRegistration(typeof(TestView), typeof(TestViewModel), nameof(TestView)),
                new UIViewRegistration(typeof(SecondTestView), typeof(SecondTestViewModel), nameof(SecondTestView)),
            };

            var preload = resolver.PreloadSet(all);

            Assert.AreEqual(1, preload.Count);
            Assert.AreEqual(typeof(TestView), preload[0].ViewType);
        }

        [Test]
        public void PreloadSet_NullInput_ReturnsEmpty()
            => Assert.AreEqual(0, new UIViewPolicyResolver(null).PreloadSet(null).Count);

        // --- Boot validation ----------------------------------------------------------------

        // A policy key matching no registered view is silently inert; the symptom (a view that
        // quietly stopped being resident) is far removed from the cause (a rename or typo).
        [Test]
        public void ValidateAgainst_UnknownKey_LogsWarning()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("matches no"));

            var resolver = new UIViewPolicyResolver(Config(Entry("TypoedViewName", resident: true)));
            resolver.ValidateAgainst(new List<UIViewRegistration>
            {
                new UIViewRegistration(typeof(TestView), typeof(TestViewModel), nameof(TestView)),
            });
        }

        // Counts warnings directly rather than relying on the test runner to fail on unexpected
        // logs — it only does that for Error/Assert/Exception, so a spurious WARNING (exactly what
        // this guards) would otherwise slip through and the test would pass vacuously.
        private static int CountWarningsDuring(Action action)
        {
            var warnings = 0;
            Application.LogCallback handler = (_, __, type) =>
            {
                if (type == LogType.Warning) warnings++;
            };
            Application.logMessageReceived += handler;
            try { action(); }
            finally { Application.logMessageReceived -= handler; }
            return warnings;
        }

        [Test]
        public void ValidateAgainst_AllKeysKnown_LogsNoWarning()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(nameof(TestView), resident: true)));
            var registrations = new List<UIViewRegistration>
            {
                new UIViewRegistration(typeof(TestView), typeof(TestViewModel), nameof(TestView)),
            };

            Assert.AreEqual(0, CountWarningsDuring(() => resolver.ValidateAgainst(registrations)));
        }

        [Test]
        public void ValidateAgainst_UnknownKey_LogsExactlyOneWarningPerKey()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry("TypoOne"), Entry("TypoTwo"),
                                                           Entry(nameof(TestView))));
            var registrations = new List<UIViewRegistration>
            {
                new UIViewRegistration(typeof(TestView), typeof(TestViewModel), nameof(TestView)),
            };

            var warnings = CountWarningsDuring(() => resolver.ValidateAgainst(registrations));

            Assert.AreEqual(2, warnings, "One warning per unknown key; the known key must stay silent.");
        }

        // The null-config path short-circuits on _byKey.Count == 0 before ever reading
        // `registrations`, so it cannot exercise the null-registrations guard. This does.
        [Test]
        public void ValidateAgainst_NullRegistrationsWithPopulatedConfig_DoesNotThrow()
        {
            var resolver = new UIViewPolicyResolver(Config(Entry(nameof(TestView), resident: true)));
            Assert.DoesNotThrow(() => resolver.ValidateAgainst(null));
        }

        [Test]
        public void ValidateAgainst_NoConfig_DoesNotThrow()
            => Assert.DoesNotThrow(() => new UIViewPolicyResolver(null).ValidateAgainst(null));
    }
}
