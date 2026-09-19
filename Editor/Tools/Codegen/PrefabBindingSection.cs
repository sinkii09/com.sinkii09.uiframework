using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    /// <summary>
    /// The "generate bindings from a prefab" half of the View/ViewModel wizard. Lives in its own
    /// type so the wizard window stays small, and so this package does not grow a second entry
    /// point — the existing wizard remains the single door.
    /// </summary>
    public sealed class PrefabBindingSection
    {
        private GameObject _prefab;
        private readonly ScanOptions _options = new();
        private List<ScannedCandidate> _candidates = new();
        private Vector2 _scroll;
        private string _status;
        private MessageType _statusType = MessageType.Info;

        public void Draw(string viewName)
        {
            EditorGUILayout.Space();
            GUILayout.Label("Generate bindings from prefab", EditorStyles.boldLabel);

            _prefab = (GameObject)EditorGUILayout.ObjectField("Prefab", _prefab, typeof(GameObject), false);
            _options.ShowAllComponents = EditorGUILayout.Toggle("Show all components", _options.ShowAllComponents);
            _options.DescendIntoNestedPrefabs = EditorGUILayout.Toggle("Descend into nested prefabs", _options.DescendIntoNestedPrefabs);

            using (new EditorGUI.DisabledScope(_prefab == null))
                if (GUILayout.Button("Scan Prefab"))
                    Scan(viewName);

            if (_candidates.Count > 0) DrawCandidates(viewName);

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, _statusType);
        }

        private void DrawCandidates(string viewName)
        {
            EditorGUILayout.Space();
            var selected = _candidates.Count(c => c.Selected);
            GUILayout.Label($"{selected} of {_candidates.Count} selected", EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(260));
            foreach (var candidate in _candidates)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    candidate.Selected = EditorGUILayout.Toggle(candidate.Selected, GUILayout.Width(18));
                    GUILayout.Label(candidate.Binding.FieldName, GUILayout.Width(160));
                    GUILayout.Label(ShortTypeName(candidate.Binding.TypeFullName), EditorStyles.miniLabel, GUILayout.Width(120));
                    GUILayout.Label(candidate.Binding.ChildPath, EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUI.DisabledScope(selected == 0))
                if (GUILayout.Button($"Generate {viewName}{PrefabBindingGenerator.GeneratedSuffix}"))
                    Generate(viewName);
        }

        private void Scan(string viewName)
        {
            var previous = ReadPreviousGeneration(viewName, out var mismatchWarning);

            // The manifest is passed INTO the scan, not applied after it: it is the authority on
            // what each (child, component) pair is already called. Naming after the fact would let
            // a newly inserted child take a name the prefab has already serialised elsewhere.
            _candidates = PrefabBindingScanner.Scan(_prefab, _options, previous);

            if (previous != null) RestorePreviousSelection(previous);

            _statusType = mismatchWarning != null ? MessageType.Warning : MessageType.Info;
            _status = mismatchWarning
                      ?? (_candidates.Count > 0 ? $"Found {_candidates.Count} candidate(s)."
                          : _options.ShowAllComponents
                              ? "No components found on any child."
                              : "No bindable children found. Try \"Show all components\".");
        }

        /// <summary>
        /// The previous generation's manifest, but only when it describes THIS prefab. A mismatch
        /// means the .g.cs was generated from a different prefab — usually an orphan left by a view
        /// rename — which the plan requires the wizard to surface rather than silently ignore.
        /// </summary>
        private List<BindingCandidate> ReadPreviousGeneration(string viewName, out string mismatchWarning)
        {
            mismatchWarning = null;
            if (!PrefabBindingGenerator.TryReadExisting(viewName, out var guid, out var previous)) return null;

            var currentGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_prefab));
            if (string.IsNullOrEmpty(guid) || guid != currentGuid)
            {
                mismatchWarning =
                    $"An existing {viewName}{PrefabBindingGenerator.GeneratedSuffix} was generated from a " +
                    "different prefab. Nothing was pre-ticked, and generating now replaces it entirely.";
                return null;
            }

            return previous;
        }

        /// <summary>
        /// Re-ticks whatever the previous generation emitted, matched on child path + type rather
        /// than on field name so a renamed field does not silently drop its binding.
        /// </summary>
        private void RestorePreviousSelection(List<BindingCandidate> previous)
        {
            var emitted = new HashSet<string>(previous.Select(BindingManifest.PairKey));
            foreach (var candidate in _candidates)
                candidate.Selected = emitted.Contains(BindingManifest.PairKey(candidate.Binding));
        }

        private void Generate(string viewName)
        {
            var chosen = _candidates.Where(c => c.Selected).Select(c => c.Binding).ToList();
            var result = PrefabBindingGenerator.Generate(viewName, _prefab, chosen);

            _status = result.Message;
            _statusType = result.Success ? MessageType.Info : MessageType.Error;
            if (result.Success) Debug.Log($"[UIFramework] {result.Message}");
        }

        private static string ShortTypeName(string fullName)
        {
            var dot = fullName.LastIndexOf('.');
            return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
        }
    }
}
