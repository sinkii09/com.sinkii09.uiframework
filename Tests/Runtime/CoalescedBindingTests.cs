using System;
using System.Collections.Generic;
using NUnit.Framework;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Sinkii09.UIFramework.Tests
{
    public class CoalescedBindingTests
    {
        private FakeFrameProvider _frames;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _frames = new FakeFrameProvider();
            UIBindingExtensions.Scheduler = null;
        }

        [TearDown]
        public void TearDown()
        {
            // The hook is static: a leaked scheduler would silently coalesce a later test that
            // expects immediate delivery.
            UIBindingExtensions.Scheduler = null;

            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private GameObject NewGameObject(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        private CoalescedBinding<T> Bind<T>(Observable<T> source, Action<T> apply, Object target = null)
            => new CoalescedBinding<T>(source, _frames, apply, target);

        // --- 1. leading edge is synchronous, for both source shapes -------------------------

        [Test]
        public void FirstValue_FromReactiveProperty_AppliesSynchronously()
        {
            var got = new List<int>();
            var rp = new ReactiveProperty<int>(42);

            using var binding = Bind<int>(rp, got.Add);

            Assert.That(got, Is.EqualTo(new[] { 42 }),
                "The first value must apply on subscribe. Deferring it makes an async-created view " +
                "flash unbound state for a frame.");
        }

        [Test]
        public void FirstValue_FromSubject_AppliesSynchronously()
        {
            var got = new List<int>();
            var subject = new Subject<int>();

            using var binding = Bind<int>(subject, got.Add);
            Assert.That(got, Is.Empty, "A Subject emits nothing on subscribe.");

            subject.OnNext(7);
            Assert.That(got, Is.EqualTo(new[] { 7 }), "The first ARRIVING value is the leading edge.");
        }

        // --- 2. the latch is one-shot, not per idle period ----------------------------------

        [Test]
        public void LeadingEdge_IsOneShot_NotPerIdlePeriod()
        {
            var got = new List<int>();
            var subject = new Subject<int>();
            using var binding = Bind<int>(subject, got.Add);

            subject.OnNext(1);                 // leading edge, synchronous
            _frames.Advance(3);                // go fully idle
            got.Clear();

            subject.OnNext(2);
            Assert.That(got, Is.Empty,
                "A per-idle-period latch would reproduce R3's ThrottleFirstLastFrame shape, which " +
                "in 1.3.1 emits default(T) after a legitimate value.");

            _frames.Advance();
            Assert.That(got, Is.EqualTo(new[] { 2 }));
        }

        // --- 3 & 4. coalescing proper --------------------------------------------------------

        [Test]
        public void ManySetsInOneFrame_ApplyOnce_WithTheLastValue()
        {
            var got = new List<int>();
            var rp = new ReactiveProperty<int>(0);
            using var binding = Bind<int>(rp, got.Add);
            got.Clear();                       // discard the leading edge

            for (int i = 1; i <= 40; i++) rp.Value = i;
            Assert.That(got, Is.Empty, "Nothing is written until the frame is pumped.");

            _frames.Advance();
            Assert.That(got, Is.EqualTo(new[] { 40 }));
        }

        [Test]
        public void QuietFrame_AppliesNothing()
        {
            var got = new List<int>();
            var rp = new ReactiveProperty<int>(0);
            using var binding = Bind<int>(rp, got.Add);
            got.Clear();

            _frames.Advance(5);
            Assert.That(got, Is.Empty);
        }

        // --- 5. the R3 ThrottleFirstLastFrame defect must not reappear ----------------------

        [Test]
        public void NeverAppliesDefaultValue_AfterALegitimateValue()
        {
            var got = new List<int>();
            var subject = new Subject<int>();
            using var binding = Bind<int>(subject, got.Add);

            // The exact schedule that makes R3 1.3.1's ThrottleFirstLastFrame emit [99, 0]:
            // a burst that closes a window, then a lone value in the next one.
            subject.OnNext(1);
            subject.OnNext(2);
            subject.OnNext(40);
            _frames.Advance();

            got.Clear();
            subject.OnNext(99);
            _frames.Advance();
            _frames.Advance();

            Assert.That(got, Is.EqualTo(new[] { 99 }),
                "A trailing default(T) here is a score label flashing 0 and a fill bar snapping to empty.");
            CollectionAssert.DoesNotContain(got, 0);
        }

        // --- 6. C-1: re-entrant apply must not strand a value --------------------------------

        [Test]
        public void ApplyThatPushesBackIntoTheSource_FlushesThatValueNextPump()
        {
            var got = new List<int>();
            var rp = new ReactiveProperty<int>(0);
            bool pushedBack = false;

            using var binding = Bind<int>(rp, v =>
            {
                got.Add(v);
                // BindTwoWay's real shape: writing the UI fires onValueChanged, which assigns
                // back into the property — landing INSIDE MoveNext.
                if (v == 5 && !pushedBack)
                {
                    pushedBack = true;
                    rp.Value = 6;
                }
            });
            got.Clear();

            rp.Value = 5;
            _frames.Advance();
            Assert.That(got, Is.EqualTo(new[] { 5 }));

            _frames.Advance();
            Assert.That(got, Is.EqualTo(new[] { 5, 6 }),
                "Without the post-apply dirty re-check the binding goes idle HOLDING 6, and the UI " +
                "stays stale until some unrelated later emission.");
        }

        [Test]
        public void ReEntrantPush_DuringTheLeadingEdge_IsAlsoDelivered()
        {
            var got = new List<int>();
            var subject = new Subject<int>();
            bool pushedBack = false;

            using var binding = Bind<int>(subject, v =>
            {
                got.Add(v);
                if (!pushedBack) { pushedBack = true; subject.OnNext(2); }
            });

            subject.OnNext(1);
            Assert.That(got, Is.EqualTo(new[] { 1 }),
                "The leading edge applies synchronously and the push-back is SCHEDULED, not recursed.");

            _frames.Advance();
            Assert.That(got, Is.EqualTo(new[] { 1, 2 }),
                "_isFirst is cleared BEFORE the first apply so a re-entrant push takes the " +
                "scheduling branch instead of re-entering the leading-edge branch.");
        }

        [Test]
        public void PushBackDuringSubscribeTimeEmission_MatchesAPlainSubscribe()
        {
            // R3 adds the observer to ReactiveProperty's list AFTER SubscribeCore's initial
            // emission. A value pushed back DURING that emission therefore reaches nobody — with
            // or without coalescing. Pinned as PARITY rather than as a literal value, so the test
            // says what coalescing owns and what is simply R3's semantics.
            List<int> RunCoalesced()
            {
                var got = new List<int>();
                var rp = new ReactiveProperty<int>(1);
                bool pushed = false;
                using var b = Bind<int>(rp, v =>
                {
                    got.Add(v);
                    if (!pushed) { pushed = true; rp.Value = 2; }
                });
                _frames.Advance();
                return got;
            }

            List<int> RunImmediate()
            {
                var got = new List<int>();
                var rp = new ReactiveProperty<int>(1);
                bool pushed = false;
                using var d = rp.Subscribe(v =>
                {
                    got.Add(v);
                    if (!pushed) { pushed = true; rp.Value = 2; }
                });
                return got;
            }

            List<int> coalesced = RunCoalesced();
            List<int> immediate = RunImmediate();

            Assert.That(coalesced, Is.EqualTo(immediate),
                "Coalescing must neither cause nor paper over an R3 semantic. If this ever fails, " +
                "coalescing has changed subscribe-time re-entrancy and that IS a regression.");
            Assert.That(coalesced, Is.EqualTo(new[] { 1 }),
                "Documents today's R3 1.3.1 answer; the parity assertion above is the real guard.");
        }

        // --- 10. a throwing apply is contained, not fatal ------------------------------------

        [Test]
        public void ThrowingApply_IsLogged_AndTheBindingKeepsWorking()
        {
            var got = new List<int>();
            var rp = new ReactiveProperty<int>(0);
            bool shouldThrow = true;

            using var binding = Bind<int>(rp, v =>
            {
                if (shouldThrow && v == 1) throw new InvalidOperationException("boom");
                got.Add(v);
            });
            got.Clear();

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            rp.Value = 1;
            _frames.Advance();

            shouldThrow = false;
            rp.Value = 2;
            _frames.Advance();

            Assert.That(got, Is.EqualTo(new[] { 2 }),
                "Dropping a faulting binding would silently stop this element updating for the " +
                "rest of the view's life.");
        }

        // --- 14 & 15. disposal and destroyed targets ----------------------------------------

        [Test]
        public void AfterDispose_NothingIsApplied()
        {
            var got = new List<int>();
            var rp = new ReactiveProperty<int>(0);
            var binding = Bind<int>(rp, got.Add);
            got.Clear();

            rp.Value = 1;
            binding.Dispose();
            _frames.Advance();

            Assert.That(got, Is.Empty);
        }

        [Test]
        public void DestroyedUnityTarget_DropsTheBinding_BeforeTouchingIt()
        {
            var go = NewGameObject("target");
            var got = new List<string>();
            var rp = new ReactiveProperty<int>(0);

            // The apply DEREFERENCES the target, so this proves the stated rationale rather than
            // merely that nothing was appended: reaching it after destruction throws
            // MissingReferenceException, which the test runner would surface as an unhandled log.
            using var binding = Bind<int>(rp, _ => got.Add(go.name), go);
            got.Clear();

            rp.Value = 1;
            Object.DestroyImmediate(go);
            _spawned.Remove(go);

            _frames.Advance();

            Assert.That(got, Is.Empty,
                "A value written during a view's life can outlive the view; the binding must be " +
                "dropped before the apply runs, not spam MissingReferenceException every frame.");
        }

        // --- 8, 9, 17. UIBindingExtensions wiring and defaults -------------------------------

        [Test]
        public void NullScheduler_DegradesToImmediate()
        {
            UIBindingExtensions.Scheduler = null;

            var go = NewGameObject("label");
            var label = go.AddComponent<TextMeshProUGUI>();
            var rp = new ReactiveProperty<int>(0);

            using var d = rp.BindToText(label, v => v.ToString());
            rp.Value = 5;

            Assert.That(label.text, Is.EqualTo("5"),
                "No scheduler must mean synchronous delivery — this is what keeps every existing " +
                "EditMode test on pre-v3.0.0 semantics.");
        }

        [Test]
        public void BindToText_IsCoalescedByDefault()
        {
            var scheduler = new FakeRenderScheduler();
            UIBindingExtensions.Scheduler = scheduler;

            var go = NewGameObject("label");
            var label = go.AddComponent<TextMeshProUGUI>();
            var rp = new ReactiveProperty<int>(0);

            using var d = rp.BindToText(label, v => v.ToString());
            Assert.That(label.text, Is.EqualTo("0"), "Leading edge still lands immediately.");

            rp.Value = 5;
            Assert.That(label.text, Is.EqualTo("0"), "Subsequent values wait for the pump.");

            scheduler.Fake.Advance();
            Assert.That(label.text, Is.EqualTo("5"));
        }

        [Test]
        public void BindToFillAmount_IsCoalescedByDefault()
        {
            // The other of only two coalesced defaults, and a real behaviour change for TheEnd's
            // health bar — worth pinning explicitly rather than inferring from BindToText.
            var scheduler = new FakeRenderScheduler();
            UIBindingExtensions.Scheduler = scheduler;

            var go = NewGameObject("bar");
            var image = go.AddComponent<Image>();
            var rp = new ReactiveProperty<float>(1f);

            using var d = rp.BindToFillAmount(image);
            Assert.That(image.fillAmount, Is.EqualTo(1f), "Leading edge lands immediately.");

            rp.Value = 0.25f;
            Assert.That(image.fillAmount, Is.EqualTo(1f), "Subsequent values wait for the pump.");

            scheduler.Fake.Advance();
            Assert.That(image.fillAmount, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void InputPathBindings_AreImmediateByDefault()
        {
            var scheduler = new FakeRenderScheduler();
            UIBindingExtensions.Scheduler = scheduler;

            var activeTarget = NewGameObject("active");
            var buttonGo = NewGameObject("button");
            var button = buttonGo.AddComponent<Button>();
            var groupGo = NewGameObject("group");
            var group = groupGo.AddComponent<CanvasGroup>();

            var activeProp = new ReactiveProperty<bool>(true);
            var interactableProp = new ReactiveProperty<bool>(true);
            var alphaProp = new ReactiveProperty<float>(1f);

            using var d1 = activeProp.BindToActive(activeTarget);
            using var d2 = interactableProp.BindToInteractable(button);
            using var d3 = alphaProp.BindToAlpha(group);

            activeProp.Value = false;
            interactableProp.Value = false;
            alphaProp.Value = 0f;

            // No pump in between — these must already have landed.
            Assert.That(activeTarget.activeSelf, Is.False,
                "A coalesced SetActive(false) leaves the object raycastable for the rest of the frame.");
            Assert.That(button.interactable, Is.False,
                "A one-frame-late interactable=false leaves the button clickable.");
            Assert.That(group.alpha, Is.EqualTo(0f),
                "Alpha is animated by DOTweenUIAnimator; coalescing would change which writer wins.");
            Assert.That(scheduler.Fake.RegisterCalls, Is.Zero,
                "Immediate bindings must not touch the scheduler at all.");
        }

        [Test]
        public void GenericBindTo_IsImmediateByDefault()
        {
            var scheduler = new FakeRenderScheduler();
            UIBindingExtensions.Scheduler = scheduler;

            var accumulated = new List<int>();
            var rp = new ReactiveProperty<int>(0);
            var sink = new object();

            using var d = rp.BindTo(sink, (v, _) => accumulated.Add(v));
            accumulated.Clear();

            rp.Value = 1;
            rp.Value = 2;
            rp.Value = 3;

            Assert.That(accumulated, Is.EqualTo(new[] { 1, 2, 3 }),
                "The setter is arbitrary caller code; coalescing DROPS intermediates, which is " +
                "silent data loss for an accumulating setter.");
        }

        [Test]
        public void ExplicitCoalescedMode_OverridesAnImmediateDefault()
        {
            var scheduler = new FakeRenderScheduler();
            UIBindingExtensions.Scheduler = scheduler;

            var groupGo = NewGameObject("group");
            var group = groupGo.AddComponent<CanvasGroup>();
            var rp = new ReactiveProperty<float>(1f);

            using var d = rp.BindToAlpha(group, UIBindMode.Coalesced);
            rp.Value = 0f;

            Assert.That(group.alpha, Is.EqualTo(1f), "Opted into coalescing, so it waits.");
            scheduler.Fake.Advance();
            Assert.That(group.alpha, Is.EqualTo(0f));
        }
    }
}
