using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    public class ViewViewModelCreatorWizard : EditorWindow
    {
        private string _viewName = "MyView";
        private string _namespace = "Game.UI";
        private string _outputFolder = "Assets/_Project/UIFramework/Views";

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
