using System;
using System.Linq;
using NUnit.Framework;
using Sinkii09.UIFramework.Editor;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests.Editor
{
    public class SoundValidatorTests
    {
        // Distinct paths with one FNV-1a hash (0x9761A54F), found by brute force.
        private const string CollidingA = "test/c191548";
        private const string CollidingB = "test/c1895126";

        private SoundAssetFixture _fx;
        private SoundGroup _ui;
        private AudioClip _clip;

        [SetUp]
        public void SetUp()
        {
            _fx = new SoundAssetFixture("SoundValidatorTests");
            _ui = _fx.Group("Ui");
            _clip = _fx.Clip("click");
        }

        [TearDown]
        public void TearDown() => _fx.Dispose();

        // Resident bank under Resources: key "ValidatorBank". Theme cue and a sounded button.
        private void BuildCleanGame(string themeClick = "ui/click", string residentKey = "ValidatorBank")
        {
            var click = _fx.Event("click-event", "ui/click", _ui, _clip);
            _fx.Bank("Resources/ValidatorBank", click);
            _fx.Config("AudioConfig", _fx.Theme("Theme", themeClick), residentKey);
        }

        private SoundValidationReport Validate() => SoundValidator.Validate(_fx.Root);

        private static void AssertError(SoundValidationReport report, string fragment) =>
            Assert.That(report.Errors.Any(e => e.Message.Contains(fragment)), report.ToString());

        [Test]
        public void Validate_CleanGame_HasNoErrors()
        {
            BuildCleanGame();
            _fx.Prefab("Sounded", go => { go.AddComponent<Button>(); go.AddComponent<UIButtonSound>(); });

            var report = Validate();

            Assert.That(report.Errors, Is.Empty, report.ToString());
        }

        [Test]
        public void Validate_ButtonWithoutUIButtonSound_IsError()
        {
            BuildCleanGame();
            _fx.Prefab("Bare", go => go.AddComponent<Button>());

            AssertError(Validate(), "has no UIButtonSound");
        }

        [Test]
        public void Validate_DropdownTemplateItemToggle_IsNotAClickable()
        {
            BuildCleanGame();
            _fx.Prefab("Dropdown", go =>
            {
                go.AddComponent<Dropdown>();
                go.AddComponent<UIButtonSound>();
                var item = new GameObject("Item", typeof(RectTransform));
                item.transform.SetParent(go.transform, false);
                item.AddComponent<Toggle>();
            });

            var report = Validate();

            Assert.That(report.Errors, Is.Empty, report.ToString());
        }

        [Test]
        public void Validate_ThemeCueWithNoEvent_IsError()
        {
            BuildCleanGame(themeClick: "ui/missing");

            AssertError(Validate(), "'ui/missing' matches no event");
        }

        [Test]
        public void Validate_ThemeCueInNonResidentBank_IsError()
        {
            BuildCleanGame();
            var later = _fx.Event("later-event", "ui/later", _ui, _clip);
            _fx.Bank("LaterBank", later);
            _fx.Theme("LaterTheme", "ui/later");

            AssertError(Validate(), "'ui/later' is not in a resident bank");
        }

        [Test]
        public void Validate_ResidentKeyMatchingNoBank_IsError()
        {
            BuildCleanGame(residentKey: "Nope");

            AssertError(Validate(), "'Nope' matches no SoundBank");
        }

        [Test]
        public void Validate_EventWithoutClipsOrGroup_IsError()
        {
            BuildCleanGame();
            _fx.Bank("EmptyBank", _fx.Event("empty", "ui/empty", null));

            var report = Validate();

            AssertError(report, "No clips");
            AssertError(report, "No group");
        }

        [Test]
        public void Validate_HashCollisionAndDuplicatePath_AreErrors()
        {
            Assert.AreEqual(SoundId.FromPath(CollidingA), SoundId.FromPath(CollidingB), "Fixture pair must collide.");
            BuildCleanGame();
            _fx.Event("a", CollidingA, _ui, _clip);
            _fx.Event("b", CollidingB, _ui, _clip);
            _fx.Event("dup", "ui/click", _ui, _clip);

            var report = Validate();

            AssertError(report, "same hash");
            AssertError(report, "Duplicate path 'ui/click'");
        }

        [Test]
        public void Validate_SfxClipCompressedInMemory_IsLoadTypeError()
        {
            BuildCleanGame();
            var compressed = _fx.Clip("compressed", AudioClipLoadType.CompressedInMemory);
            _fx.Event("compressed-event", "ui/compressed", _ui, compressed);

            AssertError(Validate(), "loads as CompressedInMemory");
        }

        [Test]
        public void Validate_FeelSoundManagerInPrefab_IsError()
        {
            var soundManager = Type.GetType("MoreMountains.Tools.MMSoundManager, MoreMountains.Tools");
            if (soundManager == null) Assert.Ignore("Feel is not installed in this project.");
            BuildCleanGame();
            _fx.Prefab("Feel", go => go.AddComponent(soundManager));

            AssertError(Validate(), "not Feel");
        }

        [Test]
        public void Validate_CodegenNeverGenerated_IsStaleError()
        {
            BuildCleanGame();
            var settings = ScriptableObject.CreateInstance<SoundIdsCodegenSettings>();
            settings.TargetAssembly = CodegenTestTarget.Asmdef();
            _fx.Save("SoundIdsCodegenSettings", settings);

            AssertError(Validate(), "stale");
        }
    }
}
