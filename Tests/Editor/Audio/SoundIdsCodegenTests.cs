using NUnit.Framework;
using Sinkii09.UIFramework.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // The target is this test assembly's asmdef, and nothing is ever written: a generated .cs file
    // would start a compile in the middle of the run.
    internal static class CodegenTestTarget
    {
        public static AssemblyDefinitionAsset Asmdef()
        {
            foreach (string guid in AssetDatabase.FindAssets("Sinkii09.UIFramework.Tests.Editor t:AssemblyDefinitionAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && asset.name == "Sinkii09.UIFramework.Tests.Editor") return asset;
            }
            Assert.Fail("Test asmdef not found.");
            return null;
        }
    }

    public class SoundIdsCodegenTests
    {
        private SoundAssetFixture _fx;
        private SoundGroup _ui;
        private SoundIdsCodegenSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _fx = new SoundAssetFixture("SoundIdsCodegenTests");
            _ui = _fx.Group("Ui");
            _settings = ScriptableObject.CreateInstance<SoundIdsCodegenSettings>();
            _settings.TargetAssembly = CodegenTestTarget.Asmdef();
            _fx.Save("SoundIdsCodegenSettings", _settings);
        }

        [TearDown]
        public void TearDown() => _fx.Dispose();

        [TestCase("ui/click", "Ui_Click")]
        [TestCase("ui/popup-show", "Ui_PopupShow")]
        [TestCase("sfx/2x/coin", "Sfx__2x_Coin")]
        [TestCase("music/main theme", "Music_MainTheme")]
        [TestCase("ui//click/", "Ui_Click")]
        public void MemberName_Path_IsPascalSegmentsJoinedByUnderscore(string path, string expected) =>
            Assert.AreEqual(expected, SoundIdNaming.MemberName(path));

        [Test]
        public void MemberName_OnlySeparators_IsNull() => Assert.IsNull(SoundIdNaming.MemberName("/-/"));

        [Test]
        public void Emit_Events_SortedByPathInAssemblyNamespace()
        {
            _fx.Event("b", "ui/b", _ui);
            _fx.Event("a", "ui/a", _ui);

            var result = SoundIdsCodegen.Emit(_settings);

            Assert.That(result.Errors, Is.Empty);
            StringAssert.Contains("namespace Sinkii09.UIFramework.Tests.Editor", result.Text);
            int a = result.Text.IndexOf("Ui_A = SoundId.FromPath(\"ui/a\");", System.StringComparison.Ordinal);
            int b = result.Text.IndexOf("Ui_B = SoundId.FromPath(\"ui/b\");", System.StringComparison.Ordinal);
            Assert.That(a, Is.GreaterThan(0));
            Assert.That(b, Is.GreaterThan(a));
            StringAssert.EndsWith("/Generated/SoundIds.g.cs", result.OutputPath);
        }

        [Test]
        public void Emit_HashCollision_RefusesWithError()
        {
            _fx.Event("a", "test/c191548", _ui);
            _fx.Event("b", "test/c1895126", _ui);

            var result = SoundIdsCodegen.Emit(_settings);

            Assert.IsNull(result.Text);
            Assert.That(result.Errors, Has.Some.Contains("same hash"));
        }

        [Test]
        public void Emit_TwoPathsOneMemberName_RefusesWithError()
        {
            _fx.Event("a", "ui/popup-show", _ui);
            _fx.Event("b", "ui/popup show", _ui);

            Assert.That(SoundIdsCodegen.Emit(_settings).Errors, Has.Some.Contains("both become 'Ui_PopupShow'"));
        }

        [Test]
        public void SameGeneratedText_IgnoresLineEndingsAndTrailingWhitespace()
        {
            Assert.IsTrue(SoundIdsCodegen.SameGeneratedText("a\nb\n", "a\r\nb\r\n\r\n"));
            Assert.IsFalse(SoundIdsCodegen.SameGeneratedText("a\nb\n", "a\nc\n"));
        }

        [Test]
        public void IsStale_FileMissing_IsTrue()
        {
            _fx.Event("a", "ui/a", _ui);

            Assert.IsTrue(SoundIdsCodegen.IsStale(_settings));
        }
    }
}
