using System;
using System.Collections.Generic;
using UnityEditor;

namespace Sinkii09.UIFramework.Editor
{
    // Asset lookups and the SoundRef walk shared by codegen, the validator and the rename tool.
    internal static class SoundAssetQueries
    {
        public static IEnumerable<(T asset, string path)> Assets<T>(string folder) where T : UnityEngine.Object
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null) yield return (asset, path);
            }
        }

        public static IEnumerable<(SoundEvent asset, string path)> Events(string folder) => Assets<SoundEvent>(folder);

        public static IEnumerable<string> AssetPaths(string filter, string folder)
        {
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { folder }))
                yield return AssetDatabase.GUIDToAssetPath(guid);
        }

        public static bool IsUnder(string assetPath, string folder) =>
            assetPath != null && assetPath.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal);

        /// <summary>Calls <paramref name="visit"/> with the <c>_path</c> property of every SoundRef in the object.</summary>
        /// <remarks>
        /// On an object inside a prefab instance, only overridden values are visited. An inherited value
        /// belongs to the source prefab: reporting it twice is noise, and writing it would turn it into a
        /// spurious override that stops following the source.
        /// </remarks>
        public static void ForEachSoundRef(SerializedObject serialized, Action<SerializedProperty> visit)
        {
            bool inInstance = PrefabUtility.IsPartOfPrefabInstance(serialized.targetObject);
            var iterator = serialized.GetIterator();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = iterator.propertyType != SerializedPropertyType.String;
                if (iterator.type != nameof(SoundRef)) continue;

                enterChildren = false;
                var path = iterator.FindPropertyRelative("_path");
                if (path == null || (inInstance && !path.prefabOverride)) continue;
                visit(path);
            }
        }
    }
}
