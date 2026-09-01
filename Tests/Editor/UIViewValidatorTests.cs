using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// Covers the unassigned-serialized-reference guard.
    ///
    /// <para>The failure this prevents is a <c>NullReferenceException</c> thrown later from inside a
    /// binding lambda, whose stack trace points at framework code rather than at the prefab that is
    /// actually misconfigured. The guard is only useful if it is both complete (finds private fields
    /// declared on a base view class) and quiet (does not fire on fields where null is meaningful),
    /// so both properties are pinned here.</para>
    /// </summary>
    public class UIViewValidatorTests
    {
        private readonly List<GameObject> _spawned = new();

        private T NewSubject<T>() where T : MonoBehaviour
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [SetUp]
        public void SetUp()
        {
            // Reports are suppressed after the first one per view type, which is session state —
            // without this reset the tests would silently depend on each other's execution order.
            UIViewValidator.ResetReportedTypesForTests();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ReportsUnassignedSerializedReference_NamingTheField()
        {
            var subject = NewSubject<MissingRefSubject>();

            LogAssert.Expect(LogType.Error, new Regex(@"_requiredTarget.*unassigned|unassigned.*_requiredTarget"));
            UIViewValidator.ValidateSerializedRefs(subject);
        }

        [Test]
        public void IgnoresFieldMarkedOptional()
        {
            var subject = NewSubject<OptionalRefSubject>();

            // No LogAssert.Expect — TearDown's NoUnexpectedReceived fails the test if anything logs.
            UIViewValidator.ValidateSerializedRefs(subject);
        }

        [Test]
        public void IgnoresAssignedReference()
        {
            var subject = NewSubject<MissingRefSubject>();
            subject.AssignTarget(subject.transform);

            UIViewValidator.ValidateSerializedRefs(subject);
        }

        [Test]
        public void IgnoresNonUnityObjectFields()
        {
            // A null string or a zeroed int is not a missing reference; only UnityEngine.Object
            // fields are checkable, and reporting anything else would be noise.
            var subject = NewSubject<NonUnityFieldSubject>();

            UIViewValidator.ValidateSerializedRefs(subject);
        }

        [Test]
        public void FindsPrivateSerializedFieldDeclaredOnBaseClass()
        {
            // The regression this pins: Type.GetFields does not return private fields of base types,
            // so a single non-DeclaredOnly reflection call misses exactly this shape — a shared view
            // base class holding its own private [SerializeField] refs.
            var subject = NewSubject<DerivedWithInheritedRefSubject>();

            LogAssert.Expect(LogType.Error, new Regex(@"_baseOnlyTarget"));
            UIViewValidator.ValidateSerializedRefs(subject);
        }

        [Test]
        public void ReportsEveryMissingFieldInOneMessage()
        {
            // One error per view, not one per field: a view with eight unassigned refs should not
            // bury the console under eight separate errors.
            var subject = NewSubject<TwoMissingRefsSubject>();

            LogAssert.Expect(LogType.Error, new Regex(@"_first[\s\S]*_second|_second[\s\S]*_first"));
            UIViewValidator.ValidateSerializedRefs(subject);
        }

        [Test]
        public void ReportsOncePerViewType_NotOncePerCall()
        {
            // A misconfigured prefab is wrong once, not once per instantiation. Without dedup a view
            // recreated after cache eviction would re-log on every recreation, and the factory
            // backstop below would double every report.
            var subject = NewSubject<TwoMissingRefsSubject>();

            LogAssert.Expect(LogType.Error, new Regex(@"_first|_second"));
            UIViewValidator.ValidateSerializedRefs(subject);
            UIViewValidator.ValidateSerializedRefs(subject);
        }

        // --- integration with UIViewBase ----------------------------------------------------
        // These two pin the UIViewBase edits themselves.
        //
        // Awake must be invoked explicitly here: EditMode is not play mode, so Unity does NOT call
        // Awake when AddComponent runs. Relying on it would make these tests pass no matter what
        // the validator did — which is exactly the false confidence they exist to prevent.

        [Test]
        public void UIViewBaseTransitionFieldsAreExemptFromValidation()
        {
            // The highest-blast-radius regression in this feature: dropping [UIOptional] from
            // UIViewBase._showTransition/_hideTransition would make EVERY transition-less view in
            // EVERY consuming project log an error. Nothing else in this suite catches it.
            var subject = NewSubject<TransitionlessViewSubject>();

            // No LogAssert.Expect — TearDown's NoUnexpectedReceived fails on any log.
            UIViewValidator.ValidateSerializedRefs(subject);
        }

        [Test]
        public void AwakeInvokesValidation()
        {
            // Pins the wiring itself: UIViewBase.Awake must call the validator. Without this, the
            // guard could be silently unhooked and every other test here would still pass.
            var subject = NewSubject<ViewWithMissingRefSubject>();
            MethodInfo awake = typeof(ViewWithMissingRefSubject)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(awake, Is.Not.Null, "UIViewBase.Awake is no longer reachable as a protected member.");

            LogAssert.Expect(LogType.Error, new Regex(@"_requiredAnchor"));
            awake.Invoke(subject, null);
        }

        // --- subjects -------------------------------------------------------------------------
        // Deliberately plain MonoBehaviours: the validator's contract is MonoBehaviour, and using
        // UIViewBase here would drag in its abstract internal members for no added coverage.

        private class MissingRefSubject : MonoBehaviour
        {
            [SerializeField] private Transform _requiredTarget;

            internal void AssignTarget(Transform t) => _requiredTarget = t;
        }

        private class OptionalRefSubject : MonoBehaviour
        {
            [SerializeField, UIOptional] private Transform _optionalTarget;
        }

        private class NonUnityFieldSubject : MonoBehaviour
        {
            [SerializeField] private int _count;
            [SerializeField] private string _label;
        }

        private class BaseWithPrivateRefSubject : MonoBehaviour
        {
            [SerializeField] private Transform _baseOnlyTarget;
        }

        private class DerivedWithInheritedRefSubject : BaseWithPrivateRefSubject
        {
        }

        private class TwoMissingRefsSubject : MonoBehaviour
        {
            [SerializeField] private Transform _first;
            [SerializeField] private Transform _second;
        }

        // Real UIViewBase subclasses. InitializeNonGenericAsync is `internal abstract` on the base
        // and reachable here only through InternalsVisibleTo.

        private class TransitionlessViewSubject : UIViewBase
        {
            internal override UniTask InitializeNonGenericAsync(
                IViewModel viewModel, IObjectResolver scope, CancellationToken ct)
                => UniTask.CompletedTask;
        }

        private class ViewWithMissingRefSubject : UIViewBase
        {
            [SerializeField] private Transform _requiredAnchor;

            internal override UniTask InitializeNonGenericAsync(
                IViewModel viewModel, IObjectResolver scope, CancellationToken ct)
                => UniTask.CompletedTask;
        }
    }
}
