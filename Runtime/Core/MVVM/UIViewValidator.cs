using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Reports unassigned serialized <see cref="UnityEngine.Object"/> references on a view, naming
    /// the field and the view, instead of letting a NullReferenceException surface later from inside
    /// a binding lambda where the stack trace points at the framework rather than at the prefab that
    /// is actually wrong.
    ///
    /// <para>Scope is deliberately narrow: only fields whose type derives from
    /// <see cref="UnityEngine.Object"/> are checked. Collections of references (<c>Image[]</c>,
    /// <c>List&lt;Button&gt;</c>) and nested <c>[Serializable]</c> classes are NOT inspected — an
    /// empty collection is usually legitimate, so reporting it would train the reader to ignore
    /// this message.</para>
    ///
    /// <para>References are expected to come from the prefab, so validation runs at Awake. A field
    /// populated later (by a spawner, after <c>Instantiate</c>) must be marked
    /// <see cref="UIOptionalAttribute"/> or it will be reported while it is still legitimately null.
    /// The framework's own <c>UIViewFactory</c> never assigns serialized fields — VContainer injects
    /// <c>[Inject]</c> members, not <c>[SerializeField]</c> ones — so this does not affect it.</para>
    ///
    /// <para>Editor and development builds only. This is an authoring guard, not a runtime cost:
    /// the call sites are stripped by <see cref="System.Diagnostics.ConditionalAttribute"/> and the
    /// body plus all static state compile away, leaving an empty method.</para>
    /// </summary>
    public static class UIViewValidator
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Reflection is walked once per concrete view type, not once per instance. Views are
        // instantiated repeatedly (every push, every cache miss) and the field set never changes.
        private static readonly Dictionary<Type, FieldInfo[]> _checkableFields = new();

        // One report per view type per session. The misconfiguration lives in the prefab, so a
        // second instance of the same type carries the same fault — logging it again is noise, and
        // a view recreated after eviction would otherwise report on every recreation. Only types
        // that actually produced a report are recorded, so a type whose first instance was fine is
        // still reported if a later instance is not.
        private static readonly HashSet<Type> _reportedTypes = new();

        private const BindingFlags DeclaredInstance =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
#endif

        /// <summary>
        /// Logs one error listing every unassigned serialized reference on <paramref name="view"/>.
        /// The view is passed as the log context, so clicking the message pings it in the Hierarchy.
        /// Safe to call more than once for the same view type — only the first report is emitted.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void ValidateSerializedRefs(MonoBehaviour view)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (view == null) return;

            Type viewType = view.GetType();

            // Allocated only once something is actually missing — this runs on every view creation
            // and the overwhelmingly common case is a correctly wired prefab.
            List<string> missing = null;
            foreach (FieldInfo field in GetCheckableFields(viewType))
            {
                // Unity's overloaded == is required here: a destroyed UnityEngine.Object is not
                // reference-null but must still be reported as missing.
                var value = field.GetValue(view) as UnityEngine.Object;
                if (value == null)
                    (missing ??= new List<string>()).Add($"'{field.Name}' ({field.FieldType.Name})");
            }

            if (missing == null) return;
            if (!_reportedTypes.Add(viewType)) return;

            var sb = new StringBuilder();
            sb.Append("[UIFramework] ").Append(viewType.Name)
              .Append(" has ").Append(missing.Count)
              .Append(missing.Count == 1 ? " unassigned serialized reference: " : " unassigned serialized references: ");
            sb.Append(string.Join(", ", missing));
            sb.Append(". Assign in the Inspector, or mark with [UIOptional] if null is intentional.");

            Debug.LogError(sb.ToString(), view);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Per-type report suppression is session state, which would otherwise leak between tests
        // and make them order-dependent.
        internal static void ResetReportedTypesForTests() => _reportedTypes.Clear();

        /// <summary>
        /// Fields Unity serializes AND that hold a UnityEngine.Object reference, across the whole
        /// inheritance chain.
        ///
        /// The base-type walk is load-bearing: GetFields does not return private fields of base
        /// types, so a private [SerializeField] on a shared view base class would be invisible to a
        /// single non-DeclaredOnly call. This codebase has been bitten by exactly that blind spot
        /// before.
        /// </summary>
        private static FieldInfo[] GetCheckableFields(Type viewType)
        {
            if (_checkableFields.TryGetValue(viewType, out var cached)) return cached;

            var fields = new List<FieldInfo>();
            for (Type t = viewType; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(DeclaredInstance))
                {
                    if (!IsSerializedByUnity(f)) continue;
                    if (!typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType)) continue;
                    if (f.IsDefined(typeof(UIOptionalAttribute), inherit: false)) continue;
                    fields.Add(f);
                }
            }

            var result = fields.ToArray();
            _checkableFields[viewType] = result;
            return result;
        }

        // Unity serializes public fields, and private/protected ones marked [SerializeField].
        // [NonSerialized] opts a public field out, and a readonly field is never serialized —
        // reporting one would be unfixable, since the Inspector cannot assign it.
        private static bool IsSerializedByUnity(FieldInfo f)
        {
            if (f.IsInitOnly) return false;
            if (f.IsDefined(typeof(NonSerializedAttribute), inherit: false)) return false;
            if (f.IsPublic) return true;
            return f.IsDefined(typeof(SerializeField), inherit: false);
        }
#endif
    }
}
