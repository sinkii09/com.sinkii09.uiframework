using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework.Tests
{
    // UIBackdrop placement. Views are bare runtime GameObjects parented under a stand-in layer
    // transform — no prefabs, no navigator, matching the suite's existing style.
    public sealed class UIBackdropTests
    {
        private GameObjectTracker _tracker;
        private RectTransform _layer;
        private UIViewPolicyConfig _config;
        private UIBackdrop _backdrop;

        [SetUp]
        public void SetUp()
        {
            _tracker = new GameObjectTracker();
            _layer = (RectTransform)_tracker.Track(new GameObject("PopupLayer", typeof(RectTransform))).transform;

            _config = ScriptableObject.CreateInstance<UIViewPolicyConfig>();
            _config.Entries = new List<UIViewPolicyEntry>
            {
                new UIViewPolicyEntry
                {
                    ViewKey = nameof(TestView),
                    Policy = new UIViewPolicy { NeedsBackdrop = true }
                }
            };

            _backdrop = new UIBackdrop(new UIViewPolicyResolver(_config), null);
        }

        [TearDown]
        public void TearDown()
        {
            _backdrop.Dispose();
            if (_config != null) Object.DestroyImmediate(_config);
            _tracker.DestroyAll();
        }

        // Builds a view of type T parented under the stand-in layer. Active by default: the
        // backdrop only dims for views that are on screen (or explicitly pending).
        private T ViewUnderLayer<T>(bool active = true) where T : UIViewBase
        {
            var view = _tracker.Track(new GameObject(typeof(T).Name, typeof(RectTransform))).AddComponent<T>();
            view.transform.SetParent(_layer, false);
            view.gameObject.SetActive(active);
            return view;
        }

        private bool BackdropVisible =>
            _backdrop.InstanceForTests != null && _backdrop.InstanceForTests.activeSelf;

        [Test]
        public void ViewWithBackdropPolicy_BackdropIsParentedUnderSameLayer()
        {
            var view = ViewUnderLayer<TestView>();

            _backdrop.Refresh(view);

            var instance = _backdrop.InstanceForTests;
            Assert.IsNotNull(instance);
            Assert.IsTrue(instance.activeSelf);
            Assert.AreSame(_layer, instance.transform.parent);
        }

        // The whole point: the dim must sit between the view and everything under it.
        [Test]
        public void Backdrop_SitsDirectlyBelowItsView()
        {
            var view = ViewUnderLayer<TestView>();

            _backdrop.Refresh(view);

            var backdropIndex = _backdrop.InstanceForTests.transform.GetSiblingIndex();
            Assert.AreEqual(backdropIndex + 1, view.transform.GetSiblingIndex(),
                "The view must render immediately on top of its backdrop.");
        }

        // A cached view shown a second time keeps a stale sibling index (ReparentToLayer only runs
        // on first creation), so without normalisation it can end up beneath a newer sibling —
        // and therefore beneath its own backdrop.
        [Test]
        public void ViewWithStaleSiblingIndex_IsRaisedAboveNewerSiblings()
        {
            var view = ViewUnderLayer<TestView>();
            var newer = _tracker.Track(new GameObject("NewerSibling", typeof(RectTransform)));
            newer.transform.SetParent(_layer, false);

            Assert.Less(view.transform.GetSiblingIndex(), newer.transform.GetSiblingIndex(),
                "Precondition: the view starts below the newer sibling.");

            _backdrop.Refresh(view);

            Assert.Greater(view.transform.GetSiblingIndex(), newer.transform.GetSiblingIndex(),
                "The backdrop-using view must be raised to the top of its layer.");
        }

        [Test]
        public void ViewWithoutBackdropPolicy_BackdropStaysHidden()
        {
            var view = ViewUnderLayer<SecondTestView>();

            _backdrop.Refresh(view);

            Assert.IsTrue(_backdrop.InstanceForTests == null || !_backdrop.InstanceForTests.activeSelf);
        }

        [Test]
        public void NullTop_HidesBackdrop()
        {
            _backdrop.Refresh(ViewUnderLayer<TestView>());
            Assert.IsTrue(_backdrop.InstanceForTests.activeSelf, "Precondition: shown first.");

            _backdrop.Refresh(null);

            Assert.IsFalse(_backdrop.InstanceForTests.activeSelf);
        }

        [Test]
        public void RepeatedRefresh_ReusesTheSameGameObject()
        {
            var first = ViewUnderLayer<TestView>();
            _backdrop.Refresh(first);
            var instance = _backdrop.InstanceForTests;

            _backdrop.Refresh(null);
            _backdrop.Refresh(ViewUnderLayer<TestView>());

            Assert.AreSame(instance, _backdrop.InstanceForTests, "Backdrop must be created once and reused.");
        }

        [Test]
        public void Backdrop_StretchesToFillAndSwallowsClicks()
        {
            _backdrop.Refresh(ViewUnderLayer<TestView>());

            var rect = (RectTransform)_backdrop.InstanceForTests.transform;
            Assert.AreEqual(Vector2.zero, rect.anchorMin);
            Assert.AreEqual(Vector2.one, rect.anchorMax);
            Assert.AreEqual(Vector2.zero, rect.offsetMin);
            Assert.AreEqual(Vector2.zero, rect.offsetMax);
            Assert.IsTrue(_backdrop.InstanceForTests.GetComponent<Image>().raycastTarget,
                "The backdrop must block clicks aimed past the popup.");
        }

        // An unparented view has nowhere sensible to host a backdrop; attaching to the scene root
        // would be worse than showing nothing.
        [Test]
        public void ViewWithNoParent_HidesInsteadOfAttachingToRoot()
        {
            var orphan = _tracker.Track(new GameObject(nameof(TestView), typeof(RectTransform))).AddComponent<TestView>();

            _backdrop.Refresh(orphan);

            Assert.IsTrue(_backdrop.InstanceForTests == null || !_backdrop.InstanceForTests.activeSelf);
        }

        // The softlock guard. If OnHideAsync throws, UIViewBase deactivates the view but
        // NavigationStack.PopAsync rethrows BEFORE removing it, so the navigator refreshes against
        // a deactivated top-of-stack view. Dimming there would cover the screen with a raycast
        // blocker and nothing above it to dismiss.
        [Test]
        public void DeactivatedView_DoesNotShowBackdrop()
        {
            var view = ViewUnderLayer<TestView>(active: false);

            _backdrop.Refresh(view);

            Assert.IsFalse(BackdropVisible, "A deactivated view must never raise a full-screen dim.");
        }

        // The complement: UINavigator refreshes with the incoming view BEFORE ShowAsync activates
        // it, so the pending path must still dim (that is what covers the entrance animation).
        [Test]
        public void PendingView_ShowsBackdropEvenWhileInactive()
        {
            var view = ViewUnderLayer<TestView>(active: false);

            _backdrop.Refresh(view, isPending: true);

            Assert.IsTrue(BackdropVisible);
        }

        // The commonest real transition: a dimmed popup closes back to a plain screen.
        [Test]
        public void BackdropView_ThenPlainView_HidesBackdrop()
        {
            _backdrop.Refresh(ViewUnderLayer<TestView>());
            Assert.IsTrue(BackdropVisible, "Precondition: shown first.");

            _backdrop.Refresh(ViewUnderLayer<SecondTestView>());

            Assert.IsFalse(BackdropVisible);
        }

        // Guards against the backdrop creeping above its view across repeated refreshes — the
        // failure mode of the first implementation, which moved the backdrop to the view's index.
        [Test]
        public void RepeatedRefreshOfSameView_KeepsBackdropBelowIt()
        {
            var view = ViewUnderLayer<TestView>();

            for (int i = 0; i < 3; i++)
            {
                _backdrop.Refresh(view);
                Assert.AreEqual(_backdrop.InstanceForTests.transform.GetSiblingIndex() + 1,
                                view.transform.GetSiblingIndex(),
                                $"Ordering must be stable across refreshes (iteration {i}).");
            }
        }

        [Test]
        public void NoPolicyResolver_NeverShows()
        {
            var bare = new UIBackdrop(new UIViewPolicyResolver(null), null);
            try
            {
                bare.Refresh(ViewUnderLayer<TestView>());
                Assert.IsTrue(bare.InstanceForTests == null || !bare.InstanceForTests.activeSelf);
            }
            finally { bare.Dispose(); }
        }
    }
}
