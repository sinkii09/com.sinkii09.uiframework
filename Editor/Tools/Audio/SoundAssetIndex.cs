using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;

namespace Sinkii09.UIFramework.Editor
{
    // What the validator knows about one game root: its events, which are in a bank, and which are in
    // a resident bank (loaded at boot, so UI cues can play on the first click).
    internal sealed class SoundAssetIndex
    {
        public readonly string Root;
        public readonly List<(SoundEvent asset, string path)> Events = new();
        public readonly HashSet<SoundEvent> Banked = new();
        public readonly HashSet<SoundEvent> Resident = new();
        private readonly Dictionary<string, List<SoundEvent>> _byPath = new();

        private SoundAssetIndex(string root) => Root = root;

        public static SoundAssetIndex Build(string root, SoundValidationReport report)
        {
            var index = new SoundAssetIndex(root);
            foreach (var entry in SoundAssetQueries.Events(root))
            {
                index.Events.Add(entry);
                if (string.IsNullOrEmpty(entry.asset.Path)) continue;
                if (!index._byPath.TryGetValue(entry.asset.Path, out var list))
                    index._byPath[entry.asset.Path] = list = new List<SoundEvent>();
                list.Add(entry.asset);
            }

            var banks = new List<(SoundBank asset, string path)>(SoundAssetQueries.Assets<SoundBank>(root));
            foreach (var (bank, _) in banks)
                foreach (var ev in bank.Events)
                    if (ev != null) index.Banked.Add(ev);

            foreach (var (config, configPath) in SoundAssetQueries.Assets<AudioConfig>(root))
                index.AddResident(config, configPath, banks, report);
            return index;
        }

        /// <summary>True when an event with this path sits in some bank, so it can be registered at runtime.</summary>
        public bool Resolves(string path) => Any(path, Banked);

        public bool IsResident(string path) => Any(path, Resident);

        private bool Any(string path, HashSet<SoundEvent> set)
        {
            if (string.IsNullOrEmpty(path) || !_byPath.TryGetValue(path, out var events)) return false;
            foreach (var ev in events)
                if (set.Contains(ev)) return true;
            return false;
        }

        // A key is matched only in the form the game's loader uses: a bank found under Resources passes
        // nothing when the game loads through Addressables, and the other way round.
        private void AddResident(AudioConfig config, string configPath,
            List<(SoundBank asset, string path)> banks, SoundValidationReport report)
        {
            var modes = LoaderModesUsing(config);
            if (modes.Count == 0)
            {
                report.Warning(configPath, "No UIFrameworkConfig uses this AudioConfig, so resident keys are " +
                                           "matched as both Addressables addresses and Resources paths.");
                modes.Add(LoaderMode.Addressables);
                modes.Add(LoaderMode.Resources);
            }

            foreach (string key in config.ResidentBankKeys)
            {
                bool found = false;
                foreach (var (bank, bankPath) in banks)
                {
                    if (!Matches(key, bankPath, modes)) continue;
                    found = true;
                    foreach (var ev in bank.Events)
                        if (ev != null) Resident.Add(ev);
                }
                if (!found)
                    report.Error(configPath, $"Resident bank key '{key}' matches no SoundBank under {Root} " +
                                             $"for loader mode {string.Join("/", modes)}.");
            }
        }

        private static HashSet<LoaderMode> LoaderModesUsing(AudioConfig config)
        {
            var modes = new HashSet<LoaderMode>();
            foreach (var (framework, _) in SoundAssetQueries.Assets<UIFrameworkConfig>("Assets"))
                if (framework.Audio == config) modes.Add(framework.LoaderMode);
            return modes;
        }

        private static bool Matches(string key, string bankPath, HashSet<LoaderMode> modes)
        {
            if (modes.Contains(LoaderMode.Addressables) && key == AddressOf(bankPath)) return true;
            return modes.Contains(LoaderMode.Resources) && key == ResourcesPathOf(bankPath);
        }

        private static string AddressOf(string assetPath)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings?.FindAssetEntry(AssetDatabase.AssetPathToGUID(assetPath), true)?.address;
        }

        private static string ResourcesPathOf(string assetPath)
        {
            const string marker = "/Resources/";
            int at = assetPath.LastIndexOf(marker, System.StringComparison.Ordinal);
            if (at < 0) return null;
            string relative = assetPath.Substring(at + marker.Length);
            int dot = relative.LastIndexOf('.');
            return dot < 0 ? relative : relative.Substring(0, dot);
        }
    }
}
