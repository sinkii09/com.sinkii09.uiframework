using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    public class ViewViewModelCreatorWizard : EditorWindow
    {
        private string _viewName = "MyView";
        private string _namespace = "Game.UI";
        private string _outputFolder = "Assets/UIFramework/Views";

        private void OnGUI()
        {
            GUILayout.Label("Create View + ViewModel", EditorStyles.boldLabel);
            _viewName    = EditorGUILayout.TextField("View Name",     _viewName);
            _namespace   = EditorGUILayout.TextField("Namespace",     _namespace);
            _outputFolder = EditorGUILayout.TextField("Output Folder", _outputFolder);

            EditorGUILayout.Space();

            var vmName = _viewName.EndsWith("View")
                ? _viewName[..^4] + "ViewModel"
                : _viewName + "ViewModel";

            EditorGUILayout.HelpBox($"Will create: {_viewName}.cs + {vmName}.cs", MessageType.Info);

            if (GUILayout.Button("Create"))
                Generate(_viewName, vmName, _namespace, _outputFolder);
        }

        private static void Generate(string viewName, string vmName, string ns, string folder)
        {
            // Check both target paths before writing either — avoids a partial-overwrite where
            // the View gets regenerated, the user then cancels on the ViewModel, and the pair
            // ends up inconsistent (one new, one stale).
            var existing = GetExistingTargets(folder, viewName, vmName);
            if (existing.Length > 0)
            {
                var message = "The following file(s) already exist and will be overwritten:\n\n"
                    + string.Join("\n", existing);
                if (!EditorUtility.DisplayDialog("Overwrite existing file(s)?", message, "Overwrite", "Cancel"))
                    return;
            }

            Directory.CreateDirectory(folder);

            var view = LoadTemplate("UIViewTemplate");
            var vm   = LoadTemplate("UIViewModelTemplate");
            if (view == null || vm == null) return;

            Write(folder, viewName, view
                .Replace("{VIEW_NAME}",      viewName)
                .Replace("{VIEWMODEL_NAME}", vmName)
                .Replace("{NAMESPACE}",      ns));

            Write(folder, vmName, vm
                .Replace("{VIEWMODEL_NAME}", vmName)
                .Replace("{NAMESPACE}",      ns));

            AssetDatabase.Refresh();
            Debug.Log($"[UIFramework] Created {viewName} + {vmName} in {folder}");
        }

        // Pure, no I/O writes — only existence checks. Kept side-effect-free and public so it's
        // directly testable without driving EditorUtility.DisplayDialog (no Unity-provided mock
        // for a native modal).
        public static string[] GetExistingTargets(string folder, string viewName, string vmName)
        {
            var viewPath = Path.Combine(folder, $"{viewName}.cs");
            var vmPath   = Path.Combine(folder, $"{vmName}.cs");

            var targets = new List<string>(2);
            if (File.Exists(viewPath)) targets.Add(viewPath);
            if (File.Exists(vmPath)) targets.Add(vmPath);
            return targets.ToArray();
        }

        private static void Write(string folder, string className, string code) =>
            File.WriteAllText(Path.Combine(folder, $"{className}.cs"), code);

        private static string LoadTemplate(string name)
        {
            var path  = $"Packages/com.sinkii09.uiframework/Editor/Tools/Templates/{name}.txt";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null) Debug.LogError($"[UIFramework] Template not found: {path}");
            return asset?.text;
        }
    }
}
