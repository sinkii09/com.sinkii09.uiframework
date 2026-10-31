using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    // Select clips (and optionally one SoundGroup) → one SoundEvent per clip, next to it.
    // Path = "<folder>/<clip>", lower-case, spaces as '-': Audio/Ui/Click.wav → "ui/click".
    internal static class SoundQuickCreateMenu
    {
        private const string MenuPath = "Assets/Create/UIFramework/Audio/Sound Events From Selected Clips";

        [MenuItem(MenuPath, false, 300)]
        private static void CreateFromSelection()
        {
            var clips = Selection.GetFiltered<AudioClip>(SelectionMode.Assets);
            var groups = Selection.GetFiltered<SoundGroup>(SelectionMode.Assets);
            var group = groups.Length == 1 ? groups[0] : null;

            var used = new HashSet<string>();
            foreach (var (ev, _) in SoundAssetQueries.Events("Assets")) used.Add(ev.Path);

            var created = new List<Object>();
            foreach (var clip in clips)
            {
                string clipPath = AssetDatabase.GetAssetPath(clip);
                string folder = Path.GetDirectoryName(clipPath).Replace('\\', '/');
                string soundPath = Slug(Path.GetFileName(folder)) + "/" + Slug(clip.name);
                if (!used.Add(soundPath))
                {
                    Debug.LogWarning($"[Audio] '{soundPath}' already exists; skipped {clipPath}.");
                    continue;
                }

                var ev = ScriptableObject.CreateInstance<SoundEvent>();
                ev.Path = soundPath;
                ev.Clips = new[] { clip };
                ev.Container = SoundContainerMode.Single;
                ev.Group = group;
                AssetDatabase.CreateAsset(ev, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{clip.name}.asset"));
                created.Add(ev);
            }
            AssetDatabase.SaveAssets();
            if (created.Count > 0) Selection.objects = created.ToArray();
            if (group == null && created.Count > 0)
                Debug.LogWarning("[Audio] No single SoundGroup was selected; assign Group on the new events.");
        }

        [MenuItem(MenuPath, true)]
        private static bool CanCreate() => Selection.GetFiltered<AudioClip>(SelectionMode.Assets).Length > 0;

        private static string Slug(string name) => name.Trim().ToLowerInvariant().Replace(' ', '-');
    }
}
