using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Editor
{
    internal partial class UIFrameworkInstallerWizard
    {
        private static readonly string[] StepLabels =
        {
            "1. Install NuGetForUnity",
            "2. Install R3 NuGet DLL",
            "3. Install OpenUPM packages (R3 wrapper, UniTask, VContainer)",
            "4. Install TextMeshPro",
            "5. Add VCONTAINER_UNITASK_INTEGRATION define",
            "6. Validate DOTween Pro",
            "7. Create UIRoot prefab",
            "8. Create UIFrameworkConfig asset",
            "9. Create UIFramework/ folder structure"
        };

        private const string NuGetForUnityId  = "com.github-glitchenzo.nugetforunity";
        private const string NuGetForUnityUrl = "https://github.com/GlitchEnzo/NuGetForUnity.git?path=/src/NuGetForUnity";
        // NuGetForUnity reads packages.config from the project root (same level as Assets/, Packages/)
        private const string PackagesConfigName = "packages.config";
        private const string R3Version = "1.3.1";

        // com.cysharp.r3 = Unity integration wrapper only; R3.dll core library is installed via NuGet (Step 2)
        internal static readonly Dictionary<string, string> OpenUpmDeps = new()
        {
            ["com.cysharp.unitask"]       = "2.5.11",
            ["com.cysharp.r3"]            = R3Version,
            ["jp.hadashikick.vcontainer"] = "1.18.0"
        };

        private const string OpenUpmUrl = "https://package.openupm.com";
        private static readonly string[] OpenUpmScopes = { "com.cysharp", "jp.hadashikick" };

        // Application.dataPath is Unity's authoritative Assets/ path; parent is always project root
        private static string ProjectRoot     => Path.GetDirectoryName(Application.dataPath);
        private static string ManifestPath    => Path.Combine(ProjectRoot, "Packages", "manifest.json");
        private static string PackagesConfigPath => Path.Combine(ProjectRoot, PackagesConfigName);

        private bool Step1_InstallNuGetForUnity()
        {
            string content;
            try { content = File.ReadAllText(ManifestPath); }
            catch (Exception ex) { Mark(0, Status.Failed); Log($"Step 1 FAILED: {ex.Message}"); return false; }

            // Remove malformed key from a prior failed install using line-split to avoid dangling commas
            const string malformedId = "com.github-glitchenzoNuGetForUnity.nugetforunity";
            if (content.Contains(malformedId))
            {
                var lines = content.Split('\n').ToList();
                int mi = lines.FindIndex(l => l.Contains(malformedId));
                if (mi >= 0)
                {
                    lines.RemoveAt(mi);
                    if (mi > 0)
                        lines[mi - 1] = lines[mi - 1].TrimEnd('\r').TrimEnd().TrimEnd(',');
                }
                content = string.Join("\n", lines);
            }

            if (content.Contains(NuGetForUnityId))
            { Mark(0, Status.Done); Log("Step 1: NuGetForUnity already in manifest."); return true; }

            const string marker = "\"dependencies\": {";
            int idx = content.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) { Mark(0, Status.Failed); Log("Step 1 FAILED: Cannot parse manifest.json."); return false; }
            content = content.Insert(idx + marker.Length, $"\n    \"{NuGetForUnityId}\": \"{NuGetForUnityUrl}\",");

            try { File.WriteAllText(ManifestPath, content); }
            catch (Exception ex) { Mark(0, Status.Failed); Log($"Step 1 FAILED: {ex.Message}"); return false; }

            EditorPrefs.SetBool(PendingKey, true);
            Client.Resolve();
            Mark(0, Status.Done);
            Log("Step 1: NuGetForUnity added. Unity is resolving — wizard will reopen after domain reload.");
            return false;
        }

        private bool Step2_InstallR3NuGet()
        {
            string manifest;
            try { manifest = File.ReadAllText(ManifestPath); }
            catch (Exception ex) { Mark(1, Status.Failed); Log($"Step 2 FAILED: {ex.Message}"); return false; }

            if (!manifest.Contains(NuGetForUnityId))
            { Mark(1, Status.Failed); Log("Step 2 FAILED: NuGetForUnity not in manifest — run Step 1 first."); return false; }

            if (IsR3DllPresent())
            { Mark(1, Status.Done); Log("Step 2: R3.dll already present."); return true; }

            // Write packages.config first as persistent fallback — NuGetForUnity restores from it on future project loads
            // Return value intentionally discarded: failure is logged inside EnsurePackagesConfig; TriggerNuGetInstall proceeds regardless
            EnsurePackagesConfig();

            // InstallIdentifier is synchronous: downloads, extracts, calls AssetDatabase.Refresh()
            if (!TriggerNuGetInstall())
            {
                Mark(1, Status.Failed);
                Log("Step 2 FAILED: NuGetForUnity install failed. Check console for details.\npackages.config has been written as fallback — NuGetForUnity will restore R3 on next project load.\nOr install R3 1.3.1 manually via NuGetForUnity > Manage NuGet Packages.");
                return false;
            }

            Mark(1, Status.Done);
            Log("Step 2: R3.dll installed via NuGet.");
            return true;
        }

        private static bool TriggerNuGetInstall()
        {
            try
            {
                var installerType  = FindType("NugetForUnity.NugetPackageInstaller");
                var identifierType = FindType("NugetForUnity.Models.NugetPackageIdentifier");
                var interfaceType  = FindType("NugetForUnity.Models.INugetPackageIdentifier");
                if (installerType == null || identifierType == null || interfaceType == null) return false;

                var identifier = Activator.CreateInstance(identifierType, "R3", R3Version);

                // isSlimRestoreInstall:false resolves transitive NuGet deps (e.g. System.Runtime.CompilerServices.Unsafe)
                var method = installerType.GetMethod("InstallIdentifier",
                    BindingFlags.Static | BindingFlags.Public,
                    null, new[] { interfaceType, typeof(bool), typeof(bool), typeof(bool) }, null);

                if (method == null)
                {
                    Debug.LogWarning("[UIFramework] NuGetForUnity: InstallIdentifier method signature not found — API may have changed. Falling back to packages.config restore.");
                    return false;
                }

                var result = method.Invoke(null, new object[] { identifier, true, false, true });
                return result is bool success && success;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UIFramework] NuGetForUnity install via reflection: {ex.Message}");
                return false;
            }
        }

        private bool Step3_InstallOpenUpmDeps()
        {
            string content;
            try { content = File.ReadAllText(ManifestPath); }
            catch (Exception ex) { Mark(2, Status.Failed); Log($"Step 3 FAILED: Cannot read manifest.json — {ex.Message}"); return false; }

            var toAdd = new List<KeyValuePair<string, string>>();
            foreach (var dep in OpenUpmDeps)
                if (!content.Contains(dep.Key))
                    toAdd.Add(dep);

            bool needsRegistry = !content.Contains("package.openupm.com");
            if (toAdd.Count == 0 && !needsRegistry)
            { Mark(2, Status.Done); Log("Step 3: OpenUPM deps already installed."); return true; }

            if (needsRegistry)
            {
                content = InsertScopedRegistry(content);
                if (content == null) { Mark(2, Status.Failed); Log("Step 3 FAILED: manifest.json has a malformed scopedRegistries block."); return false; }
            }

            if (toAdd.Count > 0)
            {
                const string marker = "\"dependencies\": {";
                int idx = content.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) { Mark(2, Status.Failed); Log("Step 3 FAILED: Cannot parse manifest.json."); return false; }

                int insertAt = idx + marker.Length;
                bool blockEmpty = content.Substring(insertAt).TrimStart().StartsWith("}");
                var sb = new StringBuilder();
                for (int i = 0; i < toAdd.Count; i++)
                {
                    sb.Append($"\n    \"{toAdd[i].Key}\": \"{toAdd[i].Value}\"");
                    if (i < toAdd.Count - 1 || !blockEmpty) sb.Append(",");
                }
                content = content.Insert(insertAt, sb.ToString());
            }

            try { File.WriteAllText(ManifestPath, content); }
            catch (Exception ex) { Mark(2, Status.Failed); Log($"Step 3 FAILED: Cannot write manifest.json — {ex.Message}"); return false; }

            EditorPrefs.SetBool(PendingKey, true);
            Client.Resolve();
            Mark(2, Status.Done);
            Log("Step 3: OpenUPM registry + packages added. Unity is resolving — wizard will reopen after reload.");
            return false;
        }

        // Returns null if manifest.json is too malformed to safely insert the registry
        private static string InsertScopedRegistry(string content)
        {
            var scopesJson = string.Join(",\n        ", Array.ConvertAll(OpenUpmScopes, s => $"\"{s}\""));
            var entry = $"{{\n      \"name\": \"package.openupm.com\",\n      \"url\": \"{OpenUpmUrl}\",\n      \"scopes\": [\n        {scopesJson}\n      ]\n    }}";

            const string key = "\"scopedRegistries\"";
            int keyIdx = content.IndexOf(key, StringComparison.Ordinal);
            if (keyIdx >= 0)
            {
                // Bounded search (40 chars) so a '[' in an unrelated key never mis-fires
                int searchFrom = keyIdx + key.Length;
                int window = Math.Min(40, content.Length - searchFrom);
                int arrStart = window > 0 ? content.IndexOf('[', searchFrom, window) : -1;
                if (arrStart < 0) return null;
                return content.Insert(arrStart + 1, "\n    " + entry + ",");
            }

            const string depsKey = "\"dependencies\"";
            int depsIdx = content.IndexOf(depsKey, StringComparison.Ordinal);
            if (depsIdx < 0) return null;
            return content.Insert(depsIdx, $"\"scopedRegistries\": [\n    {entry}\n  ],\n  ");
        }

        private bool Step4_InstallTextMeshPro()
        {
            if (IsTmpPresent())
            {
                // TMP assembly available — ensure Essential Resources are imported
                if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
                {
                    var utilType = FindType("TMPro.TMP_PackageUtilities");
                    var method   = utilType?.GetMethod("ImportProjectResourcesMenu",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method != null)
                        method.Invoke(null, null);
                    else
                        Log("Step 4: TMP ready. Import Essential Resources manually: Window > TextMeshPro > Import TMP Essential Resources.");
                }
                Mark(3, Status.Done); Log("Step 4: TextMeshPro ready."); return true;
            }

            // TMP not yet available — add to manifest
            string content;
            try { content = File.ReadAllText(ManifestPath); }
            catch (Exception ex) { Mark(3, Status.Failed); Log($"Step 4 FAILED: {ex.Message}"); return false; }

            if (!content.Contains("\"com.unity.textmeshpro\""))
            {
                const string marker = "\"dependencies\": {";
                int idx = content.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) { Mark(3, Status.Failed); Log("Step 4 FAILED: Cannot parse manifest.json."); return false; }
                content = content.Insert(idx + marker.Length, "\n    \"com.unity.textmeshpro\": \"3.0.9\",");

                try { File.WriteAllText(ManifestPath, content); }
                catch (Exception ex) { Mark(3, Status.Failed); Log($"Step 4 FAILED: {ex.Message}"); return false; }

                EditorPrefs.SetBool(PendingKey, true);
                Client.Resolve();
                Mark(3, Status.Done);
                Log("Step 4: TextMeshPro added to manifest. Reopen wizard after domain reload.");
                return false;
            }

            // In manifest but not compiled yet — domain reload in progress
            Mark(3, Status.Failed);
            Log("Step 4: TextMeshPro in manifest but assembly not ready — wait for domain reload, then Refresh.");
            return false;
        }

        private bool Step5_AddDefine()
        {
            string[] defines = { "VCONTAINER_UNITASK_INTEGRATION", "UNITASK_DOTWEEN_SUPPORT" };
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var defs = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
            var missing = Array.FindAll(defines, d => !defs.Contains(d));
            if (missing.Length == 0) { Mark(4, Status.Done); Log("Step 5: Defines already present."); return true; }
            var updated = string.IsNullOrEmpty(defs) ? string.Join(";", missing) : defs + ";" + string.Join(";", missing);
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, updated);
            Mark(4, Status.Done); Log($"Step 5: Added defines: {string.Join(", ", missing)}."); return true;
        }

        private bool Step6_ValidateDOTween()
        {
            if (IsDOTweenPresent()) { Mark(5, Status.Done); Log("Step 6: DOTween detected."); return true; }
            Mark(5, Status.Failed);
            Log("Step 6 FAILED: DOTween Pro not found.\nInstall from Asset Store, then Refresh and Run Setup.");
            return false;
        }

        private bool Step7_CreateUIRootPrefab()
        {
            const string prefabPath = "Assets/UIFramework/UIRoot.prefab";
            if (File.Exists(prefabPath)) { Mark(6, Status.Done); Log("Step 7: UIRoot prefab already exists."); return true; }

            EnsureFolder("Assets", "UIFramework");

            var root = new GameObject("UIRoot");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            // Keep in sync with UILayer's declaration order and with
            // UIFrameworkUIRootUpgrader.LayerSpecs, which is what actually wires these up.
            string[] layerNames = { "HUD", "Screen", "Popup", "Tooltip", "Notification", "Overlay", "Debug" };
            int[] layerOrders = { 0, 100, 200, 250, 275, 300, 400 };
            for (int i = 0; i < layerNames.Length; i++)
            {
                var child = new GameObject(layerNames[i]);
                child.transform.SetParent(root.transform, false);
                var rect = child.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var c = child.AddComponent<Canvas>();
                c.overrideSorting = true;
                c.sortingOrder = layerOrders[i];
                child.AddComponent<GraphicRaycaster>();
                child.AddComponent<CanvasGroup>();
                if (layerNames[i] == "Debug") child.SetActive(false);
            }

            var safeAreaType = FindType("Sinkii09.UIFramework.SafeAreaProvider");
            if (safeAreaType != null)
                root.AddComponent(safeAreaType);
            else
                Log("Step 7 WARNING: SafeAreaProvider not found — add it to UIRoot manually after runtime compiles.");

            var scopeType = FindType("Sinkii09.UIFramework.UIFrameworkLifetimeScope");
            if (scopeType != null)
                root.AddComponent(scopeType);
            else
                Log("Step 7 WARNING: UIFrameworkLifetimeScope not found — add it to UIRoot manually after runtime compiles.");

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            DestroyImmediate(root);
            AssetDatabase.Refresh();
            // This step cannot assign _layers itself: the installer assembly deliberately does not
            // reference the runtime assembly (it must run before that assembly compiles), so it
            // has no UIRootLayerRefs type to write into. Run the upgrader afterwards — it wires
            // every layer reference, and is also the migration path for UIRoots created before a
            // layer was added.
            Mark(6, Status.Done);
            Log("Step 7: UIRoot prefab created with SafeAreaProvider + UIFrameworkLifetimeScope. " +
                "Now run Tools/UIFramework/Upgrade UIRoot Layers to wire _layers, then assign _config in the Inspector.");
            return true;
        }

        private bool Step8_CreateConfigAsset()
        {
            const string assetPath = "Assets/UIFramework/UIFrameworkConfig.asset";
            if (File.Exists(assetPath)) { Mark(7, Status.Done); Log("Step 8: Config asset already exists."); return true; }

            var configType = FindType("Sinkii09.UIFramework.UIFrameworkConfig");
            if (configType == null)
            {
                Mark(7, Status.Done);
                Log("Step 8: UIFrameworkConfig not available yet — rerun after runtime code is added.");
                return true;
            }

            EnsureFolder("Assets", "UIFramework");
            var asset = ScriptableObject.CreateInstance(configType);
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            Mark(7, Status.Done); Log("Step 8: UIFrameworkConfig created at Assets/UIFramework/ — assign it to UIFrameworkLifetimeScope._config in the Inspector."); return true;
        }

        private void Step9_CreateFolderStructure()
        {
            EnsureFolder("Assets", "UIFramework");
            EnsureFolder("Assets/UIFramework", "Features");
            EnsureFolder("Assets/UIFramework", "Prefabs");
            EnsureFolder("Assets/UIFramework", "Views");
            EnsureFolder("Assets/UIFramework", "ViewModels");
            AssetDatabase.Refresh();
            Mark(8, Status.Done); Log("Step 9: UIFramework/ folder structure ready.");
        }

        private static bool IsTmpPresent() => FindType("TMPro.TMP_Text") != null;

        private static bool IsR3DllPresent()
        {
            foreach (var dir in new[] { "Assets/Packages", "Assets/Plugins", "Assets/NuGet" })
            {
                var full = Path.Combine(ProjectRoot, dir);
                if (Directory.Exists(full) &&
                    Directory.GetFiles(full, "R3.dll", SearchOption.AllDirectories).Length > 0)
                    return true;
            }
            return false;
        }

        private static bool EnsurePackagesConfig()
        {
            var fullPath = PackagesConfigPath;
            var r3Entry = $"  <package id=\"R3\" version=\"{R3Version}\" />";
            try
            {
                if (File.Exists(fullPath))
                {
                    var existing = File.ReadAllText(fullPath);
                    if (existing.Contains("id=\"R3\" version=")) return true;
                    existing = existing.Replace("</packages>", r3Entry + "\n</packages>");
                    File.WriteAllText(fullPath, existing);
                }
                else
                {
                    File.WriteAllText(fullPath,
                        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<packages>\n" + r3Entry + "\n</packages>");
                }
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UIFramework] packages.config write failed: {ex.Message}");
                return false;
            }
        }

        private static bool HasDefine(string define) =>
            PlayerSettings.GetScriptingDefineSymbols(
                NamedBuildTarget.FromBuildTargetGroup(
                    EditorUserBuildSettings.selectedBuildTargetGroup)).Contains(define);

        private static bool IsDOTweenPresent()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                if (asm.GetType("DG.Tweening.DOTween") != null) return true;
            return false;
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static void EnsureFolder(string parent, string folder)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{folder}"))
                AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
