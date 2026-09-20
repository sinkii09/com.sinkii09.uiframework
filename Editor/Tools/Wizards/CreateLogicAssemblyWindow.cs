using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    // Creates an engine-free logic assembly and its EditMode test assembly, plus the two csproj files
    // that let `dotnet test` run the same sources without opening Unity.
    //
    // Hand-writing these four files is possible and is how the two existing ones were made — but the
    // asmdef JSON is exactly the kind of thing that fails quietly. Miss noEngineReferences and the
    // assembly silently keeps its Unity dependency; miss the test assembly's defineConstraints and
    // its tests are never discovered. Neither produces an error to search for.
    public sealed class CreateLogicAssemblyWindow : EditorWindow
    {
        private string _featureName = "MyFeature";
        private string _rootNamespace = "MyGame.MyFeature.Logic";
        private LogicAssemblyTarget _target = LogicAssemblyTarget.ShipsWithTheGame;

        [MenuItem("Tools/UIFramework/Create Logic Assembly")]
        private static void Open()
        {
            var window = GetWindow<CreateLogicAssemblyWindow>(true, "Create Logic Assembly");
            window.minSize = new Vector2(430f, 260f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Engine-free assembly for rules and maths", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The generated assembly cannot reference UnityEngine at all — no MonoBehaviour, no " +
                "Debug.Log, no Vector3. That is what lets its tests run in a second with dotnet " +
                "instead of waiting on a domain reload, and what stops the coupling happening by " +
                "accident.\n\n" +
                "Put rules here: turn order, damage formulas, drop tables, seeded generation. Leave " +
                "anything that draws, animates or reads the frame clock in the Unity layer.",
                MessageType.Info);

            EditorGUILayout.Space();
            _featureName = EditorGUILayout.TextField("Feature name", _featureName);
            _rootNamespace = EditorGUILayout.TextField("Root namespace", _rootNamespace);
            _target = (LogicAssemblyTarget)EditorGUILayout.EnumPopup("Target", _target);

            EditorGUILayout.HelpBox(
                _target == LogicAssemblyTarget.ShipsWithTheGame
                    ? "Compiled into builds. Choose this for anything the running game needs."
                    : "Editor only — nothing here reaches a player's device. Choose this for " +
                      "simulations, balance solvers and validators.",
                MessageType.None);

            var folder = SelectedFolder();
            EditorGUILayout.LabelField("Destination", folder);

            EditorGUILayout.Space();

            // Validated before the button is even live, rather than caught during Create: an invalid
            // name reaching Directory.CreateDirectory throws from OnGUI, which then repeats on every
            // repaint and floods the console.
            var namesOk = LogicAssemblyTemplate.IsValidIdentifier(_featureName)
                          && LogicAssemblyTemplate.IsValidIdentifier(_rootNamespace);
            if (!namesOk)
                EditorGUILayout.HelpBox(
                    "Feature name and namespace must be dotted identifiers (letters, digits and " +
                    "underscores). They are written into the asmdef JSON and used as folder names.",
                    MessageType.Warning);

            using (new EditorGUI.DisabledScope(!namesOk))
            {
                if (GUILayout.Button("Create", GUILayout.Height(28f)))
                    Create(folder);
            }
        }

        // Falls back to Assets/ rather than refusing: the folder is shown above the button, so a
        // wrong one is visible before anything is written.
        private static string SelectedFolder()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path)) return path;
                if (!string.IsNullOrEmpty(path)) return Path.GetDirectoryName(path)?.Replace('\\', '/');
            }

            return "Assets";
        }

        private void Create(string folder)
        {
            var feature = _featureName.Trim();
            var logicAssembly = feature + ".Logic";
            var testAssembly = logicAssembly + ".Tests";

            var logicDir = Path.Combine(folder, feature, "Logic");
            var testDir = Path.Combine(folder, feature, "Logic.Tests");

            Directory.CreateDirectory(logicDir);
            Directory.CreateDirectory(testDir);

            Write(Path.Combine(logicDir, logicAssembly + ".asmdef"),
                LogicAssemblyTemplate.Asmdef(logicAssembly, _rootNamespace, _target));
            Write(Path.Combine(logicDir, logicAssembly + ".csproj"),
                LogicAssemblyTemplate.LogicCsproj(logicAssembly));

            Write(Path.Combine(testDir, testAssembly + ".asmdef"),
                LogicAssemblyTemplate.TestAsmdef(testAssembly, _rootNamespace + ".Tests"));
            Write(Path.Combine(testDir, testAssembly + ".csproj"),
                LogicAssemblyTemplate.TestCsproj(testAssembly, "../Logic/" + logicAssembly + ".csproj"));

            AssetDatabase.Refresh();

            // Logged rather than written to a file next to the code: these are the two traps that
            // cost an afternoon each, and this is the moment somebody is actually looking.
            Debug.Log(
                $"[UIFramework] Created {logicAssembly} in {logicDir}.\n" +
                "Two things that will not announce themselves:\n" +
                "  • Unity ships NUnit 3.5, so Assert.Multiple (NUnit 3.6+) compiles under `dotnet " +
                "test` and then does not exist in the Editor's runner. Prefer plain asserts.\n" +
                "  • On Unity 2023.1+ with UniTask 2.5.11 or newer, calling AsUniTask() in an " +
                "engine-free assembly drags in UnityEngine.CoreModule and fails with CS0012. Call " +
                "UniTaskExtensions.AsUniTask() explicitly, or keep UniTask out of the logic layer.\n" +
                $"Run the tests without Unity: dotnet test \"{Path.Combine(testDir, testAssembly + ".csproj")}\"\n" +
                "  • Those .csproj files are hand-written, but a Unity .gitignore normally excludes " +
                "*.csproj because Unity regenerates its own. Add a negation for them or they will " +
                "not be committed and the next clone will not be able to run the tests.");

            Close();
        }

        // .asmdef and .csproj are both plain text; AssetDatabase has no creator for either, and
        // writing through it would only add an import round-trip per file.
        private static void Write(string path, string contents)
        {
            if (File.Exists(path))
            {
                Debug.LogWarning($"[UIFramework] {path} already exists — left untouched.");
                return;
            }

            File.WriteAllText(path, contents);
        }
    }
}
