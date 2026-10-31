using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    /// <summary>Writes one SoundIds class per game root. See <see cref="SoundIdsCodegenSettings"/>.</summary>
    /// <remarks>
    /// Refuses to write when any path is empty or duplicated, two paths share a hash, or two paths map
    /// to one member name: a silent alias would play the wrong sound. Constants call SoundId.FromPath so
    /// the file stays readable; the hash runs once, at type initialisation.
    /// </remarks>
    public static class SoundIdsCodegen
    {
        public sealed class Result
        {
            public string Text;
            public string OutputPath;
            public readonly List<string> Errors = new();
        }

        [MenuItem("Tools/UIFramework/Audio/Generate Sound Ids")]
        public static void GenerateAll()
        {
            foreach (var settings in FindSettings("Assets")) Write(settings);
        }

        public static bool Write(SoundIdsCodegenSettings settings)
        {
            var result = Emit(settings);
            if (result.Errors.Count > 0)
            {
                Debug.LogError($"[Audio] Sound ids not generated for '{AssetDatabase.GetAssetPath(settings)}':\n- " +
                               string.Join("\n- ", result.Errors), settings);
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(result.OutputPath));
            File.WriteAllText(result.OutputPath, result.Text, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(result.OutputPath);
            return true;
        }

        /// <summary>True when the file is missing or differs from what would be generated now.</summary>
        public static bool IsStale(SoundIdsCodegenSettings settings)
        {
            var result = Emit(settings);
            if (result.OutputPath == null || !File.Exists(result.OutputPath)) return true;
            return !SameGeneratedText(result.Text ?? "", File.ReadAllText(result.OutputPath));
        }

        public static Result Emit(SoundIdsCodegenSettings settings)
        {
            var result = new Result();
            if (settings.TargetAssembly == null) { result.Errors.Add("No target assembly."); return result; }
            if (!SoundIdNaming.IsIdentifier(settings.ClassName))
            {
                result.Errors.Add($"Class name '{settings.ClassName}' is not a valid identifier.");
                return result;
            }

            string asmdefPath = AssetDatabase.GetAssetPath(settings.TargetAssembly);
            result.OutputPath = $"{Path.GetDirectoryName(asmdefPath).Replace('\\', '/')}/Generated/{settings.ClassName}.g.cs";
            string root = RootOf(settings);
            var members = CollectMembers(root, settings.ClassName, result.Errors);
            if (result.Errors.Count > 0) return result;

            string ns = string.IsNullOrEmpty(settings.Namespace) ? RootNamespace(settings) : settings.Namespace;
            result.Text = Render(root, ns, settings.ClassName, members);
            return result;
        }

        internal static string RootOf(UnityEngine.Object settings) =>
            Path.GetDirectoryName(AssetDatabase.GetAssetPath(settings)).Replace('\\', '/');

        internal static IEnumerable<SoundIdsCodegenSettings> FindSettings(string folder)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SoundIdsCodegenSettings), new[] { folder }))
                yield return AssetDatabase.LoadAssetAtPath<SoundIdsCodegenSettings>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static SortedDictionary<string, string> CollectMembers(string root, string className, List<string> errors)
        {
            var byPath = new SortedDictionary<string, string>(StringComparer.Ordinal);  // path → member
            var pathById = new Dictionary<uint, string>();
            var pathByMember = new Dictionary<string, string>();
            foreach (var (ev, assetPath) in SoundAssetQueries.Events(root))
            {
                string path = ev.Path;
                if (string.IsNullOrEmpty(path)) { errors.Add($"{assetPath}: empty path."); continue; }
                if (byPath.ContainsKey(path)) { errors.Add($"{assetPath}: duplicate path '{path}'."); continue; }

                uint id = SoundId.FromPath(path).Value;
                if (pathById.TryGetValue(id, out string other))
                    errors.Add($"{assetPath}: '{path}' has the same hash as '{other}'. Rename one.");
                pathById[id] = path;

                string member = SoundIdNaming.MemberName(path);
                if (member == null || member == className)
                    errors.Add($"{assetPath}: '{path}' gives no usable member name.");
                else if (pathByMember.TryGetValue(member, out string clash))
                    errors.Add($"{assetPath}: '{path}' and '{clash}' both become '{member}'.");
                pathByMember[member ?? path] = path;
                byPath[path] = member;
            }
            return byPath;
        }

        private static string Render(string root, string ns, string className, SortedDictionary<string, string> members)
        {
            var text = new StringBuilder();
            text.Append("// <auto-generated>\n")
                .Append($"// From the SoundEvent assets under {root}. Do not edit:\n")
                .Append("// run Tools/UIFramework/Audio/Generate Sound Ids instead.\n")
                .Append("// </auto-generated>\n")
                .Append("using Sinkii09.UIFramework;\n\n");
            string indent = "";
            if (!string.IsNullOrEmpty(ns)) { text.Append($"namespace {ns}\n{{\n"); indent = "    "; }
            text.Append($"{indent}public static class {className}\n{indent}{{\n");
            foreach (var pair in members)
                text.Append($"{indent}    public static readonly SoundId {pair.Value} = SoundId.FromPath(\"{Escape(pair.Key)}\");\n");
            text.Append($"{indent}}}\n");
            if (indent.Length > 0) text.Append("}\n");
            return text.ToString();
        }

        private static string RootNamespace(SoundIdsCodegenSettings settings) =>
            JsonUtility.FromJson<AsmdefNamespace>(settings.TargetAssembly.text)?.rootNamespace;

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // Git's autocrlf checks the file out with CRLF on Windows; that is not staleness.
        internal static bool SameGeneratedText(string emitted, string onDisk) =>
            Normalise(emitted) == Normalise(onDisk);

        private static string Normalise(string text) => text.Replace("\r\n", "\n").TrimEnd();

        [Serializable]
        private sealed class AsmdefNamespace
        {
            public string rootNamespace;
        }
    }
}
