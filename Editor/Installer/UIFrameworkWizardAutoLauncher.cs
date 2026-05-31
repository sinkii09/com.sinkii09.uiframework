using UnityEditor;

namespace Sinkii09.UIFramework.Editor
{
    [InitializeOnLoad]
    internal static class UIFrameworkWizardAutoLauncher
    {
        // Bump suffix (v1 → v2) to force wizard reopen after a major update
        private const string PrefsKey = "Sinkii09.UIFramework.WizardShown.v1";

        static UIFrameworkWizardAutoLauncher()
        {
            bool firstRun = !EditorPrefs.GetBool(PrefsKey, false);
            // EditorPrefs survives editor restarts — safe when packages take a long time to download
            bool installPending = EditorPrefs.GetBool(UIFrameworkInstallerWizard.PendingKey, false);
            if (firstRun || installPending)
                EditorApplication.delayCall += OpenWizard;
        }

        private static void OpenWizard()
        {
            EditorPrefs.SetBool(PrefsKey, true);
            UIFrameworkInstallerWizard.ShowWindow();
        }
    }
}
