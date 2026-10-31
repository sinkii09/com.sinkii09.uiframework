using NUnit.Framework;
using Sinkii09.UIFramework.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests.Editor
{
    public class SoundRenameToolTests
    {
        private SoundAssetFixture _fx;
        private SoundEvent _click;

        [SetUp]
        public void SetUp()
        {
            // No codegen settings under this root, so the rename writes no script.
            _fx = new SoundAssetFixture("SoundRenameToolTests");
            _click = _fx.Event("click", "ui/click", _fx.Group("Ui"));
        }

        [TearDown]
        public void TearDown() => _fx.Dispose();

        [Test]
        public void Rename_RewritesPrefabAndThemeRefs()
        {
            var theme = _fx.Theme("Theme", "ui/click");
            string prefab = _fx.Prefab("Custom", go =>
            {
                go.AddComponent<Button>();
                var sound = new SerializedObject(go.AddComponent<UIButtonSound>());
                sound.FindProperty("_custom._path").stringValue = "ui/click";
                sound.ApplyModifiedPropertiesWithoutUndo();
            });

            var result = SoundRenameTool.Rename(_click, "ui/tap", _fx.Root);

            Assert.IsTrue(result.Ok, result.Error);
            Assert.AreEqual(2, result.RewrittenRefs);
            Assert.AreEqual("ui/tap", _click.Path);
            Assert.AreEqual("ui/tap", theme.ButtonClick.Path);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefab).GetComponent<UIButtonSound>();
            Assert.AreEqual("ui/tap", new SerializedObject(saved).FindProperty("_custom._path").stringValue);
        }

        [Test]
        public void Rename_ToPathInUse_RefusesAndChangesNothing()
        {
            _fx.Event("other", "ui/other", null);

            var result = SoundRenameTool.Rename(_click, "ui/other", _fx.Root);

            Assert.IsFalse(result.Ok);
            Assert.AreEqual("ui/click", _click.Path);
        }

        [Test]
        public void Rename_ToCollidingHash_RefusesBeforeRewritingAnything()
        {
            _fx.Event("other", "test/c191548", null);
            var theme = _fx.Theme("Theme", "ui/click");

            var result = SoundRenameTool.Rename(_click, "test/c1895126", _fx.Root);

            StringAssert.Contains("same hash", result.Error);
            Assert.AreEqual("ui/click", theme.ButtonClick.Path);
        }
    }

    public class ViewSoundCueTests
    {
        private static readonly SoundRef ThemeCue = new("ui/popup-show");

        [Test]
        public void Resolve_DefaultOnPopupLayer_IsThemeCue() =>
            Assert.AreEqual(ThemeCue.Id, new ViewSoundCue().Resolve(UILayer.Popup, ThemeCue));

        [Test]
        public void Resolve_DefaultOnScreenLayer_IsSilent() =>
            Assert.IsFalse(new ViewSoundCue().Resolve(UILayer.Screen, ThemeCue).IsValid);

        [Test]
        public void Resolve_DefaultWithoutTheme_IsSilent() =>
            Assert.IsFalse(new ViewSoundCue().Resolve(UILayer.Popup, default).IsValid);

        [Test]
        public void Resolve_None_IsSilentEvenOnPopup() =>
            Assert.IsFalse(new ViewSoundCue { Mode = ViewSoundCueMode.None }.Resolve(UILayer.Popup, ThemeCue).IsValid);

        [Test]
        public void Resolve_Custom_IsCustomOnAnyLayer()
        {
            var cue = new ViewSoundCue { Mode = ViewSoundCueMode.Custom, Custom = new SoundRef("ui/chime") };

            Assert.AreEqual(SoundId.FromPath("ui/chime"), cue.Resolve(UILayer.Screen, ThemeCue));
        }
    }
}
