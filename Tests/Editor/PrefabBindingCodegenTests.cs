using System.Collections.Generic;
using NUnit.Framework;
using Sinkii09.UIFramework.Editor;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // Phase 6 (prefab -> View binding codegen). Covers the pure half: name mapping, the manifest
    // format that carries selection across regenerations, and the emitter's determinism.
    // The guards live in PrefabBindingGuardTests.
    public class PrefabBindingCodegenTests
    {
        private static BindingCandidate Candidate(string field, string path, string type) =>
            new() { FieldName = field, ChildPath = path, TypeFullName = type };

        // --- BindingFieldNamer ---------------------------------------------------------------

        [TestCase("health-bar", "_healthBar")]
        [TestCase("health_bar", "_healthBar")]
        [TestCase("HealthBar", "_healthBar")]
        [TestCase("Health Bar", "_healthBar")]
        [TestCase("health.bar", "_healthBar")]
        public void ToFieldName_SeparatorStyles_AllProduceTheSameCamelCaseField(string input, string expected)
        {
            Assert.AreEqual(expected, BindingFieldNamer.ToFieldName(input));
        }

        [Test]
        public void ToFieldName_LeadingDigit_StaysLegalBecauseOfTheUnderscorePrefix()
        {
            Assert.AreEqual("_2fast", BindingFieldNamer.ToFieldName("2fast"));
        }

        [Test]
        public void ToFieldName_CSharpKeyword_IsLegalWithoutEscaping()
        {
            // "_class" is a legal identifier, which is why the namer has no '@'-escape path.
            Assert.AreEqual("_class", BindingFieldNamer.ToFieldName("class"));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("###")]
        public void ToFieldName_NothingUsable_FallsBackRatherThanEmittingAnIllegalField(string input)
        {
            Assert.AreEqual("_field", BindingFieldNamer.ToFieldName(input));
        }

        [Test]
        public void ReserveUniqueFieldName_DuplicateChildNames_QualifiesByParent()
        {
            var taken = new HashSet<string>();

            var first = BindingFieldNamer.ReserveUniqueFieldName("Icon", "Header", taken);
            var second = BindingFieldNamer.ReserveUniqueFieldName("Icon", "Footer", taken);

            Assert.AreEqual("_icon", first);
            Assert.AreEqual("_footerIcon", second);
            Assert.AreNotEqual(first, second);
        }

        [Test]
        public void ReserveUniqueFieldName_ReservedInheritedMember_IsNeverEmitted()
        {
            // _showDisposables is protected on UIView<T>; shadowing it is a warning, not an error,
            // so nothing else would catch this.
            var name = BindingFieldNamer.ReserveUniqueFieldName("show-disposables", "Panel", new HashSet<string>());

            Assert.AreNotEqual("_showDisposables", name);
            Assert.IsFalse(BindingFieldNamer.IsReserved(name));
        }

        // --- BindingManifest -----------------------------------------------------------------

        [Test]
        public void Manifest_RoundTrips_ThroughItsSingleOwner()
        {
            var original = new List<BindingCandidate>
            {
                Candidate("_healthBar", "Panel/HealthBar", "UnityEngine.UI.Image"),
                Candidate("_okButton", "Panel/Buttons/OK", "UnityEngine.UI.Button")
            };

            var text = BindingManifest.Format("abc123", original);

            Assert.IsTrue(BindingManifest.TryParse(text, out var guid, out var parsed));
            Assert.AreEqual("abc123", guid);
            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual("_healthBar", parsed[0].FieldName);
            Assert.AreEqual("Panel/HealthBar", parsed[0].ChildPath);
            Assert.AreEqual("UnityEngine.UI.Image", parsed[0].TypeFullName);
            Assert.AreEqual("Panel/Buttons/OK", parsed[1].ChildPath);
        }

        [Test]
        public void Manifest_ChildPathContainingTheSeparator_SurvivesTheRoundTrip()
        {
            // A GameObject may legitimately be named "a | b", which would otherwise split the line.
            var text = BindingManifest.Format("g", new List<BindingCandidate>
            {
                Candidate("_odd", "Panel/a | b", "UnityEngine.Canvas")
            });

            Assert.IsTrue(BindingManifest.TryParse(text, out _, out var parsed));
            Assert.AreEqual("Panel/a | b", parsed[0].ChildPath);
        }

        // --- BindingPartialEmitter -----------------------------------------------------------

        [Test]
        public void Emit_IsDeterministic_SoRegeneratingAnUnchangedPrefabProducesNoDiff()
        {
            var candidates = new List<BindingCandidate> { Candidate("_a", "A", "UnityEngine.Canvas") };

            var first = BindingPartialEmitter.Emit("Game.UI", "MyView", "guid", candidates);
            var second = BindingPartialEmitter.Emit("Game.UI", "MyView", "guid", candidates);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void Emit_UsesExplicitLineFeeds_NotEnvironmentNewLine()
        {
            // Environment.NewLine would make "byte-identical on regeneration" false across machines.
            // Asserting only DoesNotContain("\r\n") would pass trivially wherever NewLine is "\n",
            // i.e. it would never fail on Linux CI — the exact platform that cannot catch the bug.
            var source = BindingPartialEmitter.Emit("Game.UI", "MyView", "g",
                new List<BindingCandidate> { Candidate("_a", "A", "UnityEngine.Canvas") });

            // A bare \r is the platform-independent assertion: it fails on Windows if the emitter
            // ever switches to Environment.NewLine, and stays meaningful on Linux.
            StringAssert.DoesNotContain("\r", source);
            StringAssert.Contains("\n", source);
        }

        [Test]
        public void Emit_OmitsTheBaseType_SoTheHandWrittenPartOwnsIt()
        {
            var source = BindingPartialEmitter.Emit("Game.UI", "MyView", "g", new List<BindingCandidate>());

            StringAssert.Contains("public partial class MyView", source);
            StringAssert.DoesNotContain(": UIView", source);
        }

        [Test]
        public void Emit_CarriesAutoGeneratedHeaderAndManifestMarker()
        {
            var source = BindingPartialEmitter.Emit("Game.UI", "MyView", "g",
                new List<BindingCandidate> { Candidate("_a", "A", "UnityEngine.Canvas") });

            StringAssert.StartsWith("// <auto-generated/>", source);
            Assert.IsTrue(BindingManifest.HasMarker(source));
            StringAssert.Contains("[SerializeField] private UnityEngine.Canvas _a;", source);
        }

        [Test]
        public void Emit_WithoutNamespace_StillProducesACompilableShape()
        {
            var source = BindingPartialEmitter.Emit("", "MyView", "g", new List<BindingCandidate>());

            StringAssert.DoesNotContain("namespace", source);
            StringAssert.Contains("public partial class MyView", source);
        }

        // --- Regression: manifest is the pair-key owner (reviewer S2) -----------------------

        [Test]
        public void PairKey_PathContainingAPipe_DoesNotAliasOntoAnotherBinding()
        {
            // A GameObject may legitimately be named "a | b". A raw string concat elsewhere would
            // make these two distinct bindings collide on one key.
            var a = BindingManifest.PairKey("Root/a | b", "UnityEngine.Canvas");
            var b = BindingManifest.PairKey("Root/a", "b | UnityEngine.Canvas");

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void PairKey_RoundTripsThroughTheManifest_SoScanAndRestoreAgree()
        {
            var candidate = Candidate("_odd", "Root/a | b", "UnityEngine.Canvas");
            var text = BindingManifest.Format("guid", new List<BindingCandidate> { candidate });

            Assert.IsTrue(BindingManifest.TryParse(text, out _, out var parsed));
            Assert.AreEqual(BindingManifest.PairKey(candidate), BindingManifest.PairKey(parsed[0]));
        }

        [Test]
        public void HasMarker_FutureFormatVersion_IsStillOurs()
        {
            // Bumping the header to v2 must not make v1 output un-overwritable by its own tool.
            Assert.IsTrue(BindingManifest.HasMarker("// uifw-bindings-manifest v2\n"));
        }
    }
}
