using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    /// <summary>Renames a SoundEvent path and every SoundRef that points at it, then regenerates ids.</summary>
    /// <remarks>
    /// Rewrites prefabs and ScriptableObjects under the root. Scenes are listed for a manual fix, not
    /// rewritten. Code using the old generated constant stops compiling, which is the point: a renamed
    /// sound cannot go silently missing.
    /// </remarks>
    public static class SoundRenameTool
    {
        public sealed class Result
        {
            public string Error;
            public int RewrittenRefs;
            /// <summary>The rename went through but sound ids could not be regenerated (see the Console).</summary>
            public bool CodegenFailed;
            public readonly List<string> ScenesToFix = new();
            public bool Ok => Error == null;
        }

        public static Result Rename(SoundEvent ev, string newPath, string rootFolder)
        {
            var result = new Result();
            string oldPath = ev.Path;
            newPath = newPath?.Trim();
            if (string.IsNullOrEmpty(newPath)) result.Error = "The new path is empty.";
            else if (newPath == oldPath) result.Error = "The path is unchanged.";
            else result.Error = Conflict(ev, newPath);
            if (!result.Ok) return result;

            if (!string.IsNullOrEmpty(oldPath))
            {
                foreach (string prefab in CandidatePaths("t:Prefab", rootFolder, oldPath))
                    result.RewrittenRefs += RewritePrefab(prefab, oldPath, newPath);
                foreach (string asset in CandidatePaths("t:ScriptableObject", rootFolder, oldPath))
                    result.RewrittenRefs += RewriteAsset(asset, oldPath, newPath);
                result.ScenesToFix.AddRange(CandidatePaths("t:Scene", rootFolder, oldPath));
            }

            var serialized = new SerializedObject(ev);
            serialized.FindProperty(nameof(SoundEvent.Path)).stringValue = newPath;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ev);
            AssetDatabase.SaveAssets();

            foreach (var settings in SoundIdsCodegen.FindSettings(rootFolder))
                if (!SoundIdsCodegen.Write(settings)) result.CodegenFailed = true;
            return result;
        }

        // Checked before anything is rewritten, so a rename codegen would refuse changes nothing.
        private static string Conflict(SoundEvent ev, string newPath)
        {
            string member = SoundIdNaming.MemberName(newPath);
            if (member == null) return $"'{newPath}' gives no usable member name.";
            var id = SoundId.FromPath(newPath);
            foreach (var (other, _) in SoundAssetQueries.Events("Assets"))
            {
                if (other == ev || string.IsNullOrEmpty(other.Path)) continue;
                if (other.Path == newPath) return $"'{newPath}' is already used by another SoundEvent.";
                if (other.Id == id) return $"'{newPath}' has the same hash as '{other.Path}'.";
                if (SoundIdNaming.MemberName(other.Path) == member)
                    return $"'{newPath}' and '{other.Path}' both become '{member}'.";
            }
            return null;
        }

        // A text pre-filter so only files that mention the old path are loaded.
        private static List<string> CandidatePaths(string filter, string root, string oldPath)
        {
            var paths = new List<string>();
            foreach (string path in SoundAssetQueries.AssetPaths(filter, root))
                if (File.ReadAllText(path).Contains(oldPath)) paths.Add(path);
            return paths;
        }

        private static int RewritePrefab(string prefabPath, string oldPath, string newPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                int count = 0;
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                    if (component != null) count += Rewrite(new SerializedObject(component), oldPath, newPath);
                if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return count;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static int RewriteAsset(string assetPath, string oldPath, string newPath)
        {
            int count = 0;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset is not ScriptableObject) continue;
                int rewritten = Rewrite(new SerializedObject(asset), oldPath, newPath);
                if (rewritten > 0) EditorUtility.SetDirty(asset);
                count += rewritten;
            }
            return count;
        }

        // Inherited values are skipped by ForEachSoundRef: the source prefab is rewritten on its own and
        // its instances keep following it.
        private static int Rewrite(SerializedObject serialized, string oldPath, string newPath)
        {
            int count = 0;
            SoundAssetQueries.ForEachSoundRef(serialized, path =>
            {
                if (path.stringValue != oldPath) return;
                path.stringValue = newPath;
                count++;
            });
            if (count > 0) serialized.ApplyModifiedPropertiesWithoutUndo();
            return count;
        }
    }
}
