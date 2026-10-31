using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    // Text field plus a picker of every SoundEvent path in the project; '/' in paths become submenus.
    // A path no event has is tinted red (the validator reports it by name).
    [CustomPropertyDrawer(typeof(SoundRef))]
    internal sealed class SoundRefDrawer : PropertyDrawer
    {
        private const float PickerWidth = 22f;
        private static List<string> s_paths;
        private static HashSet<string> s_known;

        static SoundRefDrawer() => EditorApplication.projectChanged += () => s_paths = null;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var path = property.FindPropertyRelative("_path");
            EnsurePaths();

            EditorGUI.BeginProperty(position, label, property);
            var field = new Rect(position.x, position.y, position.width - PickerWidth - 2f, position.height);
            var picker = new Rect(field.xMax + 2f, position.y, PickerWidth, position.height);

            var previous = GUI.color;
            if (!string.IsNullOrEmpty(path.stringValue) && !s_known.Contains(path.stringValue))
                GUI.color = new Color(1f, 0.55f, 0.55f);
            EditorGUI.showMixedValue = path.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            string edited = EditorGUI.TextField(field, label, path.stringValue);
            if (EditorGUI.EndChangeCheck()) path.stringValue = edited;
            EditorGUI.showMixedValue = false;
            GUI.color = previous;

            if (GUI.Button(picker, "▾")) ShowMenu(property.serializedObject, path.propertyPath, path.stringValue);
            EditorGUI.EndProperty();
        }

        private static void ShowMenu(SerializedObject serialized, string propertyPath, string current)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(current), () => Set(serialized, propertyPath, ""));
            foreach (string soundPath in s_paths)
                menu.AddItem(new GUIContent(soundPath), soundPath == current, () => Set(serialized, propertyPath, soundPath));
            menu.ShowAsContext();
        }

        private static void Set(SerializedObject serialized, string propertyPath, string value)
        {
            if (serialized.targetObject == null) return;
            serialized.Update();
            serialized.FindProperty(propertyPath).stringValue = value;
            serialized.ApplyModifiedProperties();
        }

        private static void EnsurePaths()
        {
            if (s_paths != null) return;
            s_paths = new List<string>();
            foreach (var (ev, _) in SoundAssetQueries.Events("Assets"))
                if (!string.IsNullOrEmpty(ev.Path)) s_paths.Add(ev.Path);
            s_paths.Sort(System.StringComparer.Ordinal);
            s_known = new HashSet<string>(s_paths);
        }
    }
}
