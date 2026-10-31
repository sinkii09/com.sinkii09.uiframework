using UnityEditor;

namespace Sinkii09.UIFramework.Editor
{
    /// <summary>Checks one game's sound setup. Call from the game's EditMode test:</summary>
    /// <example><code>
    /// var report = SoundValidator.Validate("Assets/Games/MyGame");
    /// Assert.That(report.Errors, Is.Empty, report.ToString());
    /// </code></example>
    /// <remarks>
    /// Scans assets under <c>rootFolder</c>: events, banks, AudioConfigs, themes, codegen settings and
    /// prefabs. Scenes are not scanned; views and their buttons are prefabs.
    /// </remarks>
    public static class SoundValidator
    {
        public static SoundValidationReport Validate(string rootFolder)
        {
            var report = new SoundValidationReport();
            if (!AssetDatabase.IsValidFolder(rootFolder))
            {
                report.Error(rootFolder, "Not a folder.");
                return report;
            }

            var index = SoundAssetIndex.Build(rootFolder.TrimEnd('/'), report);
            SoundEventChecks.Run(index, report);
            SoundPrefabChecks.Run(index, report);
            CheckThemes(index, report);
            CheckCodegen(index.Root, report);
            return report;
        }

        [MenuItem("Tools/UIFramework/Audio/Validate Sounds Under Selected Folder")]
        private static void ValidateSelection()
        {
            string folder = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (!AssetDatabase.IsValidFolder(folder)) folder = "Assets";
            var report = Validate(folder);
            if (report.Errors.Count > 0) UnityEngine.Debug.LogError("[Audio] " + report);
            else UnityEngine.Debug.Log("[Audio] " + report);
        }

        // Theme cues play from the first click, so they must be resident. A theme referenced from an
        // AudioConfig outside the root is still checked against this root's banks.
        private static void CheckThemes(SoundAssetIndex index, SoundValidationReport report)
        {
            var themes = new System.Collections.Generic.HashSet<UISoundTheme>();
            foreach (var (theme, _) in SoundAssetQueries.Assets<UISoundTheme>(index.Root)) themes.Add(theme);
            foreach (var (config, _) in SoundAssetQueries.Assets<AudioConfig>(index.Root))
                if (config.Theme != null) themes.Add(config.Theme);

            foreach (var theme in themes)
            {
                string where = AssetDatabase.GetAssetPath(theme);
                var serialized = new SerializedObject(theme);
                SoundAssetQueries.ForEachSoundRef(serialized,
                    path => SoundPrefabChecks.CheckRef(path.stringValue, true, $"{where} > {path.propertyPath}", index, report));
            }
        }

        private static void CheckCodegen(string root, SoundValidationReport report)
        {
            foreach (var settings in SoundIdsCodegen.FindSettings(root))
            {
                string where = AssetDatabase.GetAssetPath(settings);
                var result = SoundIdsCodegen.Emit(settings);
                foreach (string error in result.Errors) report.Error(where, "Codegen: " + error);
                if (result.Errors.Count == 0 && SoundIdsCodegen.IsStale(settings))
                    report.Error(where, "Generated sound ids are stale. Run Tools/UIFramework/Audio/Generate Sound Ids.");
            }
        }
    }
}
