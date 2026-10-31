using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    // SoundEvent context menu → Rename Path. The scan root is the game root (the folder of the codegen
    // settings above the event), or all of Assets when there is none.
    internal sealed class SoundRenameWindow : EditorWindow
    {
        private SoundEvent _event;
        private string _newPath;
        private string _root;
        private string _message;

        [MenuItem("CONTEXT/SoundEvent/Rename Path...")]
        private static void Open(MenuCommand command)
        {
            var ev = (SoundEvent)command.context;
            var window = GetWindow<SoundRenameWindow>(true, "Rename Sound Path");
            window._event = ev;
            window._newPath = ev.Path;
            window._root = GameRootOf(AssetDatabase.GetAssetPath(ev));
            window._message = null;
            window.minSize = new Vector2(420, 130);
        }

        private void OnGUI()
        {
            if (_event == null) { Close(); return; }
            EditorGUILayout.LabelField("Current", _event.Path);
            EditorGUILayout.LabelField("Scan root", _root);
            _newPath = EditorGUILayout.TextField("New path", _newPath);
            if (GUILayout.Button("Rename")) _message = Describe(SoundRenameTool.Rename(_event, _newPath, _root));
            if (_message != null) EditorGUILayout.HelpBox(_message, MessageType.Info);
        }

        private static string Describe(SoundRenameTool.Result result)
        {
            if (!result.Ok) return result.Error;
            string text = $"Renamed. {result.RewrittenRefs} reference(s) rewritten. " +
                          (result.CodegenFailed ? "Sound ids were NOT regenerated: see the Console." : "Sound ids regenerated.");
            if (result.ScenesToFix.Count > 0) text += "\nFix by hand in: " + string.Join(", ", result.ScenesToFix);
            return text;
        }

        private static string GameRootOf(string assetPath)
        {
            string best = "Assets";
            foreach (var settings in SoundIdsCodegen.FindSettings("Assets"))
            {
                string root = SoundIdsCodegen.RootOf(settings);
                if (SoundAssetQueries.IsUnder(assetPath, root) && root.Length > best.Length) best = root;
            }
            return best;
        }
    }
}
