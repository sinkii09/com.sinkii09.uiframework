using System.IO;
using NUnit.Framework;
using Sinkii09.UIFramework.Editor;

namespace Sinkii09.UIFramework.Tests.Editor
{
    // Regression tests for the C4 finding: ViewViewModelCreatorWizard used to overwrite an
    // existing View/ViewModel pair with no existence check, confirmation, or backup. Covers only
    // GetExistingTargets (pure, no I/O writes) — the confirm-and-abort dialog itself drives
    // EditorUtility.DisplayDialog, a native modal with no Unity-provided mock, so it is verified
    // manually rather than under automated test.
    //
    // Lives in Tests/Editor/ (its own Editor-only asmdef), not Tests/Runtime/: the wizard under
    // test is EditorWindow-based and only compiles in Sinkii09.UIFramework.Editor.Tools
    // (includePlatforms: ["Editor"]). The main Tests.asmdef has no platform restriction, and Unity
    // will not let an unrestricted assembly reference an Editor-only one.
    // See plans/260802-1122-hardening-cluster/plan.md, Item 1.
    public class ViewViewModelCreatorWizardTests
    {
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Path.Combine(Path.GetTempPath(), "UIFrameworkWizardTests_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_folder);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }

        [Test]
        public void GetExistingTargets_NeitherFileExists_ReturnsEmpty()
        {
            var result = ViewViewModelCreatorWizard.GetExistingTargets(_folder, "FooView", "FooViewModel");
            Assert.IsEmpty(result);
        }

        [Test]
        public void GetExistingTargets_OnlyViewExists_ReturnsViewPathOnly()
        {
            var viewPath = Path.Combine(_folder, "FooView.cs");
            File.WriteAllText(viewPath, "// existing");

            var result = ViewViewModelCreatorWizard.GetExistingTargets(_folder, "FooView", "FooViewModel");

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(viewPath, result[0]);
        }

        [Test]
        public void GetExistingTargets_OnlyViewModelExists_ReturnsViewModelPathOnly()
        {
            var vmPath = Path.Combine(_folder, "FooViewModel.cs");
            File.WriteAllText(vmPath, "// existing");

            var result = ViewViewModelCreatorWizard.GetExistingTargets(_folder, "FooView", "FooViewModel");

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(vmPath, result[0]);
        }

        [Test]
        public void GetExistingTargets_BothExist_ReturnsBothPaths()
        {
            var viewPath = Path.Combine(_folder, "FooView.cs");
            var vmPath = Path.Combine(_folder, "FooViewModel.cs");
            File.WriteAllText(viewPath, "// existing");
            File.WriteAllText(vmPath, "// existing");

            var result = ViewViewModelCreatorWizard.GetExistingTargets(_folder, "FooView", "FooViewModel");

            Assert.AreEqual(2, result.Length);
            CollectionAssert.Contains(result, viewPath);
            CollectionAssert.Contains(result, vmPath);
        }
    }
}
