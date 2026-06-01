using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    [CustomEditor(typeof(UIViewBase), true)]
    public class UIViewBaseEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (!Application.isPlaying) return;

            var view = (UIViewBase)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

            GUI.enabled = false;
            EditorGUILayout.TextField("View ID",    view.ViewId);
            EditorGUILayout.Toggle("Is Visible",    view.IsVisible);
            EditorGUILayout.EnumPopup("Layer",      view.Layer);
            GUI.enabled = true;
        }
    }
}
