namespace Sinkii09.UIFramework.Editor
{
    /// <summary>Whether a logic assembly is part of the shipped game or a development-only tool.</summary>
    public enum LogicAssemblyTarget
    {
        /// <summary>
        /// Compiled into builds. Combat maths, economy rules, generation — anything the running game
        /// needs. <c>includePlatforms</c> stays empty.
        /// </summary>
        ShipsWithTheGame,

        /// <summary>
        /// Editor only: simulations, balance solvers, content validators. <c>includePlatforms</c> is
        /// <c>["Editor"]</c>, so nothing here reaches a player's device.
        /// </summary>
        EditorOnlyTool,
    }

    // Emits the files for an engine-free logic assembly. Pure string work, no Unity API and no disk
    // access, so it is unit-testable — same split as PrefabBindingScanner (finds things) and
    // BindingPartialEmitter (writes text). CreateLogicAssemblyWindow does the disk half.
    //
    // What a logic assembly IS: a folder compiled into its own DLL that is FORBIDDEN from referencing
    // UnityEngine. No MonoBehaviour, no Debug.Log, no Vector3, no Time.deltaTime — the compiler
    // refuses. Two things are bought with that restriction:
    //
    //   1. Tests run without opening Unity. An Editor test run pays a domain reload; `dotnet test`
    //      against these same sources pays nothing, so a rules change can be verified in a second.
    //   2. The coupling cannot happen by accident. The compiler enforces it, not discipline.
    //
    // What belongs inside: pure functions, state machines, formulas, seeded and reproducible
    // randomness, invariants worth testing. What does not: anything that draws, animates, plays
    // sound, touches a prefab or reads the frame clock. The line is "decide what happens" versus
    // "show it happening".
    public static class LogicAssemblyTemplate
    {
        // The whole point of LangVersion 9: it is the ceiling Unity itself allows, so `record`,
        // `init` and System.HashCode fail in the dotnet build in seconds instead of at the next
        // Editor compile.
        internal const string LangVersion = "9.0";
        internal const string TargetFramework = "netstandard2.1";

        /// <summary>
        /// True for a dotted C# identifier, e.g. <c>Combat.Logic</c>.
        /// </summary>
        /// <remarks>
        /// Two holes, one check. These values are interpolated straight into JSON, so a quote or a
        /// backslash produces an asmdef Unity cannot parse — and an unparseable asmdef does not
        /// report an error, the assembly simply never exists. The feature name is also used as a
        /// folder name, where an absolute path or a <c>..</c> segment would write outside the project.
        /// </remarks>
        public static bool IsValidIdentifier(string value)
            => !string.IsNullOrWhiteSpace(value)
               && System.Text.RegularExpressions.Regex.IsMatch(
                      value, @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$");

        public static string Asmdef(string assemblyName, string rootNamespace, LogicAssemblyTarget target)
        {
            var platforms = target == LogicAssemblyTarget.EditorOnlyTool ? "\"Editor\"" : string.Empty;

            return
$@"{{
    ""name"": ""{assemblyName}"",
    ""rootNamespace"": ""{rootNamespace}"",
    ""references"": [],
    ""includePlatforms"": [{platforms}],
    ""excludePlatforms"": [],
    ""allowUnsafeCode"": false,
    ""overrideReferences"": false,
    ""precompiledReferences"": [],
    ""autoReferenced"": false,
    ""defineConstraints"": [],
    ""versionDefines"": [],
    ""noEngineReferences"": true
}}";
        }

        // Editor-only whatever the logic assembly targets: the reason to keep logic engine-free is to
        // test it without a player, so these are EditMode tests.
        public static string TestAsmdef(string assemblyName, string rootNamespace)
        {
            // Strip the SUFFIX. String.Replace(".Tests", "") would also eat a middle one, so a
            // feature called My.Tests.Thing would reference an assembly that does not exist — and an
            // asmdef reference to a missing assembly is an error nobody reads until the tests refuse
            // to compile.
            const string suffix = ".Tests";
            var target = assemblyName.EndsWith(suffix, System.StringComparison.Ordinal)
                ? assemblyName.Substring(0, assemblyName.Length - suffix.Length)
                : assemblyName;

            return
$@"{{
    ""name"": ""{assemblyName}"",
    ""rootNamespace"": ""{rootNamespace}"",
    ""references"": [
        ""{target}"",
        ""UnityEngine.TestRunner"",
        ""UnityEditor.TestRunner""
    ],
    ""includePlatforms"": [""Editor""],
    ""excludePlatforms"": [],
    ""allowUnsafeCode"": false,
    ""overrideReferences"": true,
    ""precompiledReferences"": [""nunit.framework.dll""],
    ""autoReferenced"": false,
    ""defineConstraints"": [""UNITY_INCLUDE_TESTS""],
    ""versionDefines"": [],
    ""noEngineReferences"": false
}}";
        }

        // Needs no Unity DLLs at all, which is the practical proof that the assembly really is
        // engine-free: if this stops building, something reached for UnityEngine.
        public static string LogicCsproj(string assemblyName)
        {
            return
$@"<Project Sdk=""Microsoft.NET.Sdk"">

  <!--
    Builds the same sources Unity compiles into {assemblyName}, with no Unity assemblies referenced.
    That is the point: if this project stops building, something in the folder reached for
    UnityEngine and the assembly is no longer engine-free.

    LangVersion {LangVersion} is Unity's ceiling, so `record`, `init` and System.HashCode fail here in
    seconds rather than at the next Editor compile.
  -->

  <PropertyGroup>
    <TargetFramework>{TargetFramework}</TargetFramework>
    <LangVersion>{LangVersion}</LangVersion>
    <AssemblyName>{assemblyName}</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include=""$(MSBuildThisFileDirectory)**\*.cs"" />
  </ItemGroup>

</Project>";
        }

        public static string TestCsproj(string assemblyName, string logicCsprojRelativePath)
        {
            return
$@"<Project Sdk=""Microsoft.NET.Sdk"">

  <!--
    Runs the EditMode tests outside Unity: `dotnet test`.

    Unity ships NUnit 3.5 via com.unity.ext.nunit, so anything newer used here will compile in this
    project and FAIL inside Unity. The best-known example is Assert.Multiple, which arrived in 3.6
    and does not exist in the Editor's runner. Prefer plain asserts.
  -->

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>{LangVersion}</LangVersion>
    <AssemblyName>{assemblyName}</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include=""$(MSBuildThisFileDirectory)**\*.cs"" />
    <ProjectReference Include=""{logicCsprojRelativePath}"" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include=""NUnit"" Version=""3.14.0"" />
    <PackageReference Include=""NUnit3TestAdapter"" Version=""4.5.0"" />
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.11.1"" />
  </ItemGroup>

</Project>";
        }
    }
}
