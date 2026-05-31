using System.IO;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    internal partial class UIFrameworkInstallerWizard : EditorWindow
    {
        // internal so AutoLauncher references the same constant — no duplication
        internal const string PendingKey = "Sinkii09.UIFramework.InstallPending";

        private enum Status { Pending, Done, Failed }

        private Status[] _status;
        private string _log = "";
        private Vector2 _scroll;

        [MenuItem("Tools/UIFramework/Setup Wizard")]
        public static void ShowWindow() =>
            GetWindow<UIFrameworkInstallerWizard>("UIFramework Setup").minSize = new Vector2(420, 500);

        private void OnEnable() { _status = new Status[StepLabels.Length]; RefreshStatus(); }

        private void RefreshStatus()
        {
            string manifest;
            try { manifest = File.ReadAllText(ManifestPath); }
            catch { return; } // leave all statuses at Pending if manifest is unreadable

            bool nugetInManifest = manifest.Contains("com.github-glitchenzo.nugetforunity");
            bool r3DllPresent    = IsR3DllPresent();
            bool allOpenUpm      = true;
            foreach (var dep in OpenUpmDeps) { if (!manifest.Contains(dep.Key)) { allOpenUpm = false; break; } }
            bool openUpmDone = allOpenUpm && manifest.Contains("package.openupm.com");

            // Clear pending only when all three install phases are confirmed complete
            if (nugetInManifest && r3DllPresent && openUpmDone) EditorPrefs.DeleteKey(PendingKey);

            _status[0] = nugetInManifest ? Status.Done : Status.Pending;
            _status[1] = r3DllPresent    ? Status.Done : Status.Pending;
            _status[2] = openUpmDone     ? Status.Done : Status.Pending;
            _status[3] = HasDefine("VCONTAINER_UNITASK_INTEGRATION") ? Status.Done : Status.Pending;
            _status[4] = IsDOTweenPresent() ? Status.Done : Status.Pending;
            _status[5] = File.Exists("Assets/_Project/Prefabs/UIRoot.prefab") ? Status.Done : Status.Pending;
            _status[6] = File.Exists("Assets/Resources/UIFramework/UIFrameworkConfig.asset") ? Status.Done : Status.Pending;
            _status[7] = AssetDatabase.IsValidFolder("Assets/_Project/Features") ? Status.Done : Status.Pending;
        }

        private void OnGUI()
        {
            GUILayout.Label("Sinkii09 UIFramework — Project Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            for (int i = 0; i < StepLabels.Length; i++)
            {
                var col = _status[i] == Status.Done ? Color.green : _status[i] == Status.Failed ? Color.red : Color.gray;
                var icon = _status[i] == Status.Done ? "✓" : _status[i] == Status.Failed ? "✗" : "○";
                var prev = GUI.color; GUI.color = col;
                GUILayout.Label($"  {icon}  {StepLabels[i]}");
                GUI.color = prev;
            }

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledGroupScope(AllDone()))
                if (GUILayout.Button("Run Setup", GUILayout.Height(30))) RunAll();
            if (GUILayout.Button("Refresh")) { RefreshStatus(); _log = ""; }
            EditorGUILayout.Space(6);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.HelpBox(_log.Length > 0 ? _log : "Press 'Run Setup' to begin.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void RunAll()
        {
            _log = "";
            if (!Step1_InstallNuGetForUnity()) return;
            if (!Step2_InstallR3NuGet()) return;
            if (!Step3_InstallOpenUpmDeps()) return;
            if (!Step4_AddDefine()) return;
            if (!Step5_ValidateDOTween()) return;
            if (!Step6_CreateUIRootPrefab()) return;
            if (!Step7_CreateConfigAsset()) return;
            Step8_CreateFolderStructure();
            EditorPrefs.DeleteKey(PendingKey);
            Log("Setup complete ✓");
        }

        private bool AllDone() { foreach (var s in _status) if (s != Status.Done) return false; return true; }
        private void Mark(int i, Status s) { _status[i] = s; Repaint(); }
        private void Log(string m) { _log += (_log.Length > 0 ? "\n" : "") + m; Repaint(); }
    }
}
