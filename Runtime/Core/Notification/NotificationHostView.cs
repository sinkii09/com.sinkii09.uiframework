using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Resident host for toast notifications. Place one under the UIRoot's Notification layer.
    ///
    /// <para>Extends <see cref="UIViewBase"/> rather than <c>UIView&lt;T&gt;</c> so
    /// <c>UIViewRegistry.AutoRegister</c> never picks it up: it stays off the navigation stack and
    /// is never routed through <c>UIViewFactory</c>, whose job is ViewModel and child-scope wiring
    /// that a toast host does not want. Same residency model as <c>TooltipViewBase</c> and the
    /// transition overlay.</para>
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(RectTransform))]
    public class NotificationHostView : UIViewBase
    {
        [Tooltip("Parent for spawned toast rows. Defaults to this transform when unassigned.")]
        [UIOptional]
        [SerializeField] private RectTransform _container;

        [Tooltip("Row prefab instantiated once per visible slot and reused.")]
        [SerializeField] private NotificationItemView _itemPrefab;

        private readonly List<NotificationItemView> _items = new();

        public override UILayer Layer => UILayer.Notification;

        /// <summary>Raised when the host is torn down so the service can drop its view bindings.</summary>
        internal event Action Destroyed;

        protected override void Awake()
        {
            base.Awake();

            // Pre-hide so the first frame after scene load never flashes an empty host.
            CanvasGroup.alpha = 0f;
            gameObject.SetActive(false);

            // A host that takes raycasts sits over the whole screen on layer 275 and swallows every
            // click above Popup, because the layer canvas carries a GraphicRaycaster. Belt and
            // braces, because these defeat different things: the CanvasGroup covers the subtree,
            // while raycastTarget=false survives a child with its own blocksRaycasts=true group.
            MakeNonBlocking();
            StripRaycasts(gameObject);
        }

        public override async UniTask ShowAsync(CancellationToken externalCt = default)
        {
            try
            {
                await base.ShowAsync(externalCt);
            }
            finally
            {
                // UIViewBase.ShowAsync sets interactable/blocksRaycasts = true once its setup
                // finishes, and DOTweenUIAnimator's cancel path does the same. Correct for a normal
                // view, fatal here — so re-assert in a finally, where a cancelled or faulted show
                // cannot leave the host blocking.
                MakeNonBlocking();
            }
        }

        public override async UniTask HideAsync(CancellationToken externalCt = default)
        {
            try
            {
                await base.HideAsync(externalCt);
            }
            finally
            {
                MakeNonBlocking();
            }
        }

        /// <summary>
        /// Returns the reusable row for <paramref name="index"/>, instantiating it on first use.
        /// Null when no prefab is assigned — the service treats rendering as optional and keeps
        /// its own entry list ticking regardless.
        /// </summary>
        internal NotificationItemView GetOrCreateItem(int index)
        {
            if (index < 0) return null;

            // Unity-null, not reference-null: a row destroyed underneath us must be rebuilt.
            while (_items.Count <= index) _items.Add(null);
            if (_items[index] != null) return _items[index];

            if (_itemPrefab == null) return null;

            var parent = _container != null ? _container : (RectTransform)transform;
            var item = Instantiate(_itemPrefab, parent);
            item.name = $"{_itemPrefab.name}_{index}";
            // Rows are created at runtime and so never passed through Awake's sweep above.
            StripRaycasts(item.gameObject);
            item.SetActive(false);
            _items[index] = item;
            return item;
        }

        // Not an override — UIViewBase declares no OnDestroy, so there is no base call to make.
        private void OnDestroy() => Destroyed?.Invoke();

        private void MakeNonBlocking()
        {
            if (CanvasGroup == null) return;
            CanvasGroup.interactable = false;
            CanvasGroup.blocksRaycasts = false;
        }

        // protected, not internal: subclasses live in game assemblies, and internal would make this
        // unreachable for exactly the callers that need it for their own runtime-spawned content.
        protected void StripRaycasts(GameObject target)
        {
            foreach (var graphic in target.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        // Sealed, and that is load-bearing: UIViewBase declares this `internal abstract`, so without
        // a concrete override here no assembly outside the framework could subclass this host.
        internal sealed override UniTask InitializeNonGenericAsync(
            IViewModel viewModel, IObjectResolver scope, CancellationToken ct)
            => throw new NotSupportedException(
                "NotificationHostView is a resident view, never factory-created. " +
                "Place it directly in the UIRoot hierarchy under the Notification layer.");
    }
}
