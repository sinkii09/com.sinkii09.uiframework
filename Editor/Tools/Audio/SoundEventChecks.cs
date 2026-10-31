using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    // Per-event checks: identity, content, import settings, loudness.
    internal static class SoundEventChecks
    {
        private const float LoudnessOutlierLu = 6f;
        private const int MinMeasuredForMedian = 3;
        private static readonly string[] OverridePlatforms = { "WebGL", "Android" };

        public static void Run(SoundAssetIndex index, SoundValidationReport report)
        {
            var pathById = new Dictionary<uint, string>();
            var seenPaths = new HashSet<string>();
            var loudnessByBus = new Dictionary<AudioBus, List<(float value, string path)>>();

            foreach (var (ev, assetPath) in index.Events)
            {
                CheckIdentity(ev, assetPath, pathById, seenPaths, report);
                CheckContent(ev, assetPath, index, report);
                if (ev.Group == null) continue;
                foreach (var clip in ev.Clips)
                    if (clip != null) CheckImport(ev, clip, assetPath, report);
                if (ev.MeasuredLoudness != 0f) Add(loudnessByBus, ev.Group.Bus, (ev.MeasuredLoudness, assetPath));
            }
            CheckLoudness(loudnessByBus, report);
        }

        private static void CheckIdentity(SoundEvent ev, string assetPath, Dictionary<uint, string> pathById,
            HashSet<string> seenPaths, SoundValidationReport report)
        {
            if (string.IsNullOrEmpty(ev.Path)) { report.Error(assetPath, "Empty path."); return; }
            if (!seenPaths.Add(ev.Path)) { report.Error(assetPath, $"Duplicate path '{ev.Path}'."); return; }

            uint id = ev.Id.Value;
            if (pathById.TryGetValue(id, out string other))
                report.Error(assetPath, $"'{ev.Path}' has the same hash as '{other}'. Rename one.");
            else pathById[id] = ev.Path;
        }

        private static void CheckContent(SoundEvent ev, string assetPath, SoundAssetIndex index, SoundValidationReport report)
        {
            if (ev.Clips == null || ev.Clips.Length == 0) report.Error(assetPath, "No clips.");
            else if (System.Array.IndexOf(ev.Clips, null) >= 0) report.Error(assetPath, "A clip slot is empty.");
            if (ev.Group == null) report.Error(assetPath, "No group.");
            if (!index.Banked.Contains(ev)) report.Warning(assetPath, "In no SoundBank, so it never plays.");
        }

        private static void CheckImport(SoundEvent ev, AudioClip clip, string assetPath, SoundValidationReport report)
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) is not AudioImporter importer) return;

            // SFX decode once so a play costs nothing; music stays compressed so it fits WebGL memory.
            var expected = ev.Group.Bus == AudioBus.Music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            CheckLoadType(importer.defaultSampleSettings, "default", expected, clip, assetPath, report);
            foreach (string platform in OverridePlatforms)
                if (importer.ContainsSampleSettingsOverride(platform))
                    CheckLoadType(importer.GetOverrideSampleSettings(platform), platform, expected, clip, assetPath, report);

            // Compressed loops can gap at the seam (WebGL decodes AAC with priming samples). Metadata
            // cannot tell whether the clip was padded for it, so this only asks for a device check.
            if (ev.Loop && importer.defaultSampleSettings.compressionFormat != AudioCompressionFormat.PCM)
                report.Warning(assetPath, $"Loops compressed clip '{clip.name}'. Verify the loop seam on device.");
        }

        private static void CheckLoadType(AudioImporterSampleSettings settings, string where, AudioClipLoadType expected,
            AudioClip clip, string assetPath, SoundValidationReport report)
        {
            if (settings.loadType != expected)
                report.Error(assetPath, $"Clip '{clip.name}' ({where}) loads as {settings.loadType}; this bus needs {expected}.");
        }

        private static void CheckLoudness(Dictionary<AudioBus, List<(float value, string path)>> byBus, SoundValidationReport report)
        {
            foreach (var pair in byBus)
            {
                var entries = pair.Value;
                if (entries.Count < MinMeasuredForMedian) continue;
                var values = new List<float>(entries.Count);
                foreach (var entry in entries) values.Add(entry.value);
                values.Sort();
                int mid = values.Count / 2;
                float median = values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) * 0.5f;

                foreach (var (value, path) in entries)
                    if (Mathf.Abs(value - median) > LoudnessOutlierLu)
                        report.Warning(path, $"Loudness {value:0.0} is more than {LoudnessOutlierLu} LU from the {pair.Key} median {median:0.0}.");
            }
        }

        private static void Add<TKey, TValue>(Dictionary<TKey, List<TValue>> map, TKey key, TValue value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<TValue>();
            list.Add(value);
        }
    }
}
