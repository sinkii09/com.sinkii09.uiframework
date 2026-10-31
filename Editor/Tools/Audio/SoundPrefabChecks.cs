using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Editor
{
    // Prefab checks: every clickable control declares its sound, every SoundRef resolves, UI cues are
    // resident, and Feel's sound system is not used (one audio system per game).
    internal static class SoundPrefabChecks
    {
        public static void Run(SoundAssetIndex index, SoundValidationReport report)
        {
            foreach (string prefabPath in SoundAssetQueries.AssetPaths("t:Prefab", index.Root))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (root == null) continue;
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null) continue;  // missing script
                    CheckComponent(component, prefabPath, IsReportedBySource(component, index.Root), index, report);
                }
            }
        }

        // `inherited`: the source prefab reports the component itself; only this prefab's overrides of its
        // SoundRefs are checked here (ForEachSoundRef visits overrides only on instances).
        private static void CheckComponent(Component component, string prefabPath, bool inherited,
            SoundAssetIndex index, SoundValidationReport report)
        {
            string where = $"{prefabPath} > {PathOf(component.transform)}";
            bool uiCue = component is UIButtonSound || component is UIViewBase;
            var serialized = new SerializedObject(component);
            SoundAssetQueries.ForEachSoundRef(serialized, path => CheckRef(path.stringValue, uiCue, where, index, report));
            if (inherited) return;

            if (IsClickable(component) && component.GetComponent<UIButtonSound>() == null)
                report.Error(where, $"{component.GetType().Name} has no UIButtonSound (use Silent for gameplay feedback).");
            if (component is UIButtonSound && component.GetComponent<Selectable>() == null)
                report.Error(where, "UIButtonSound needs a Selectable on the same GameObject.");
            if (IsFeelSound(component))
                report.Error(where, $"{component.GetType().Name}: game sound goes through IAudioService, not Feel.");
            if (component.GetType().Name.StartsWith("MMF_Player")) CheckFeelFeedbacks(serialized, where, report);
        }

        /// <summary>Shared by prefab and theme checks. Empty references are allowed: they play nothing.</summary>
        public static void CheckRef(string soundPath, bool mustBeResident, string where, SoundAssetIndex index,
            SoundValidationReport report)
        {
            if (string.IsNullOrEmpty(soundPath)) return;
            if (!index.Resolves(soundPath))
                report.Error(where, $"Sound '{soundPath}' matches no event in a SoundBank under {index.Root}.");
            else if (mustBeResident && !index.IsResident(soundPath))
                report.Error(where, $"UI sound '{soundPath}' is not in a resident bank, so it is silent until its bank loads.");
        }

        // A dropdown's template Item toggle is the dropdown's own row, not a separate control.
        private static bool IsClickable(Component component)
        {
            if (component is Toggle)
                return component.GetComponentInParent<Dropdown>(true) == null &&
                       component.GetComponentInParent<TMP_Dropdown>(true) == null;
            return component is Button || component is Dropdown || component is TMP_Dropdown;
        }

        // An object inherited from a nested prefab (or a variant's base) whose source is under the
        // root is reported once, by that source.
        private static bool IsReportedBySource(Component component, string root)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(component)) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(component);
            return source != null && SoundAssetQueries.IsUnder(AssetDatabase.GetAssetPath(source), root);
        }

        private static bool IsFeelSound(Component component)
        {
            var type = component.GetType();
            if (type.Namespace == null || !type.Namespace.StartsWith("MoreMountains")) return false;
            string name = type.Name;
            return name == "MMSoundManager" || name.StartsWith("MMFeedbackSound") ||
                   name.StartsWith("MMFeedbackAudioSource") || name.StartsWith("MMFeedbackMMSoundManager");
        }

        // MMF_Player keeps its feedbacks as [SerializeReference], so they are found by type name and the
        // framework needs no Feel reference.
        private static void CheckFeelFeedbacks(SerializedObject serialized, string where, SoundValidationReport report)
        {
            var iterator = serialized.GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType != SerializedPropertyType.ManagedReference) continue;
                string type = iterator.managedReferenceFullTypename;
                if (type.Contains("MMF_Sound") || type.Contains("MMF_MMSoundManager") || type.Contains("MMF_AudioSource"))
                    report.Error(where, $"Feel feedback {type}: game sound goes through IAudioService, not Feel.");
            }
        }

        private static string PathOf(Transform transform)
        {
            string path = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
    }
}
