using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    public class UIFrameworkSetupWizard : EditorWindow
    {
        private string _root = "Assets/_Project/UIFramework";

        private void OnGUI()
        {
            GUILayout.Label("UIFramework Project Setup", EditorStyles.boldLabel);
            _root = EditorGUILayout.TextField("Root Folder", _root);
            EditorGUILayout.HelpBox(
                "Creates Views/, ViewModels/, Prefabs/, Configs/ folders and a UIFrameworkConfig asset.",
                MessageType.Info);

            if (GUILayout.Button("Run Setup"))
                RunSetup(_root);
        }

        private static void RunSetup(string root)
        {
            foreach (var dir in new[] { "Views", "ViewModels", "Prefabs", "Configs" })
                Directory.CreateDirectory(Path.Combine(root, dir));

            // Import new OS folders into AssetDatabase before creating assets inside them.
            AssetDatabase.Refresh();

            var configPath = $"{root}/Configs/UIFrameworkConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<UIFrameworkConfig>(configPath) == null)
            {
                var config = ScriptableObject.CreateInstance<UIFrameworkConfig>();
                AssetDatabase.CreateAsset(config, configPath);
                AssetDatabase.Refresh();
            }
            EditorUtility.DisplayDialog("UIFramework Setup",
                $"Setup complete.\nFolder structure created at {root}.", "OK");
        }
    }
}
