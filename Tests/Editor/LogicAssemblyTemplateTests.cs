using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Sinkii09.UIFramework.Editor;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// The generated asmdef JSON, field by field.
    ///
    /// <para>Worth testing precisely because none of these mistakes announce themselves. An asmdef
    /// missing <c>noEngineReferences</c> compiles happily and keeps its Unity dependency; a test
    /// assembly missing <c>defineConstraints</c> is simply never discovered by the test runner. Both
    /// look exactly like success until somebody notices months later.</para>
    /// </summary>
    public class LogicAssemblyTemplateTests
    {
        private const string Logic = "Combat.Logic";
        private const string Tests = "Combat.Logic.Tests";

        [Test]
        public void LogicAsmdef_IsEngineFree()
        {
            // The two fields that ARE the feature. Everything else is arrangement.
            var json = JObject.Parse(
                LogicAssemblyTemplate.Asmdef(Logic, "Combat.Logic", LogicAssemblyTarget.ShipsWithTheGame));

            Assert.That(json["noEngineReferences"].Value<bool>(), Is.True);
            Assert.That(json["references"].Values<string>(), Is.Empty,
                "A reference to any other assembly can drag UnityEngine back in transitively.");
            Assert.That(json["name"].Value<string>(), Is.EqualTo(Logic));
        }

        [Test]
        public void ShippingAssembly_IsNotRestrictedToTheEditor()
        {
            // The variant that is easy to copy wrong. Getting it backwards leaves the game's own
            // rules out of the build, and it only surfaces at build time, never while coding.
            var json = JObject.Parse(
                LogicAssemblyTemplate.Asmdef(Logic, "Combat.Logic", LogicAssemblyTarget.ShipsWithTheGame));

            Assert.That(json["includePlatforms"].Values<string>(), Is.Empty);
        }

        [Test]
        public void EditorOnlyTool_NeverReachesAPlayersDevice()
        {
            var json = JObject.Parse(
                LogicAssemblyTemplate.Asmdef("Balance.Logic", "Balance.Logic", LogicAssemblyTarget.EditorOnlyTool));

            Assert.That(json["includePlatforms"].Values<string>(), Is.EqualTo(new[] { "Editor" }));
        }

        [Test]
        public void TestAsmdef_CanActuallySeeTheAssemblyItTests()
        {
            var json = JObject.Parse(LogicAssemblyTemplate.TestAsmdef(Tests, "Combat.Logic.Tests"));

            Assert.That(json["references"].Values<string>(), Contains.Item(Logic),
                "The reference name is derived by stripping '.Tests' — a rename breaks it silently.");
            Assert.That(json["defineConstraints"].Values<string>(), Contains.Item("UNITY_INCLUDE_TESTS"),
                "Without this the tests compile into player builds.");
            Assert.That(json["precompiledReferences"].Values<string>(), Contains.Item("nunit.framework.dll"));
            Assert.That(json["overrideReferences"].Value<bool>(), Is.True,
                "precompiledReferences is ignored unless overrideReferences is set.");
            Assert.That(json["includePlatforms"].Values<string>(), Is.EqualTo(new[] { "Editor" }));
        }

        [Test]
        public void LogicCsproj_PinsUnitysCeilingSoNewerCSharpFailsEarly()
        {
            var xml = XDocument.Parse(LogicAssemblyTemplate.LogicCsproj(Logic));
            var properties = xml.Root.Element("PropertyGroup");

            Assert.That(properties.Element("LangVersion").Value, Is.EqualTo("9.0"),
                "Anything higher lets `record` and `init` build here and fail in Unity.");
            Assert.That(properties.Element("TargetFramework").Value, Is.EqualTo("netstandard2.1"));
        }

        [Test]
        public void LogicCsproj_ReferencesNothingAtAll()
        {
            // The practical proof that the assembly is engine-free: the project has nothing of
            // Unity's to compile against, so a stray UnityEngine call cannot build.
            //
            // Asserted on the XML, not on the raw text. The first version searched the string for
            // "UnityEngine" and failed against the template's own explanatory comment — a test that
            // could only ever have gone green by accident, and did not.
            var xml = XDocument.Parse(LogicAssemblyTemplate.LogicCsproj(Logic));

            Assert.That(xml.Descendants("Reference"), Is.Empty);
            Assert.That(xml.Descendants("PackageReference"), Is.Empty);
            Assert.That(xml.Descendants("ProjectReference"), Is.Empty);
        }

        [Test]
        public void TestAsmdef_StripsOnlyTheTrailingTestsSuffix()
        {
            // String.Replace(".Tests", "") eats the middle one too, pointing the reference at an
            // assembly that does not exist — and nothing reports that until the tests refuse to build.
            var json = JObject.Parse(
                LogicAssemblyTemplate.TestAsmdef("My.Tests.Thing.Logic.Tests", "Ns"));

            Assert.That(json["references"].Values<string>(), Contains.Item("My.Tests.Thing.Logic"));
        }

        [TestCase("Combat")]
        [TestCase("Combat.Logic")]
        [TestCase("_Private.Thing2")]
        public void IsValidIdentifier_AcceptsDottedIdentifiers(string value)
            => Assert.That(LogicAssemblyTemplate.IsValidIdentifier(value), Is.True);

        // Each of these either breaks the generated JSON or escapes the destination folder.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("2Combat")]
        [TestCase("Combat Logic")]
        [TestCase("../../Windows")]
        [TestCase("C:/Windows")]
        [TestCase("Combat\"Logic")]
        [TestCase("Combat.")]
        public void IsValidIdentifier_RejectsAnythingThatWouldBreakJsonOrEscapeTheFolder(string value)
            => Assert.That(LogicAssemblyTemplate.IsValidIdentifier(value), Is.False);

        [Test]
        public void TestCsproj_BuildsAgainstTheLogicProject()
        {
            var xml = XDocument.Parse(
                LogicAssemblyTemplate.TestCsproj(Tests, "../Logic/Combat.Logic.csproj"));

            var reference = xml.Descendants("ProjectReference").Single();
            Assert.That(reference.Attribute("Include").Value, Is.EqualTo("../Logic/Combat.Logic.csproj"));
        }

        [Test]
        public void TestCsproj_WarnsThatUnityShipsAnOlderNUnit()
        {
            // Unity ships NUnit 3.5. Assert.Multiple arrived in 3.6, so it compiles under dotnet and
            // then does not exist in the Editor's runner — a failure with no obvious cause unless
            // somebody was told first.
            var csproj = LogicAssemblyTemplate.TestCsproj(Tests, "../Logic/Combat.Logic.csproj");

            Assert.That(csproj, Does.Contain("NUnit 3.5"));
            Assert.That(csproj, Does.Contain("Assert.Multiple"));
        }
    }
}
