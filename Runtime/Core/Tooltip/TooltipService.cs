using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    // Owns the one resident tooltip: which view is up, what it is anchored to, and the timing
    // state machine Idle -> Pending -> Shown -> Grace -> Idle.
    //
    // The machine advances in Tick() off Time.unscaledDeltaTime rather than awaiting UniTask.Delay.
    // Two reasons: this project pauses with timeScale = 0, so a scaled wait would mean no tooltip
    // ever appears in a pause menu; and a Tick-driven machine is genuinely frame-testable — tests
    // set the durations to zero and call Tick(), with no wall-clock anywhere.
    public sealed class TooltipService : ITooltipService, IInitializable, ITickable
    {
        private readonly UIRootLayerRefs _layers;
        private readonly ITransitionOverlay _overlay;
        private readonly TooltipViewIndex _index;

        // Defaults come from UIFrameworkConfig; internal so tests can zero them (InternalsVisibleTo
        // is granted to both test assemblies in Runtime/AssemblyInfo.cs). These four and no more.
        internal float _showDelay;
        internal float _hideGrace;
        internal float _reShowWindow;
        internal float _longPressThreshold;

        private float _longPressMoveCancelPixels;

        private enum State { Idle, Pending, Shown, Grace }

        private State _state;
        private float _timer;
        private float _lastShownAt = float.NegativeInfinity;

        private TooltipRequest _request;
        private TooltipViewBase _active;
        private RectTransform _anchor;
        private Vector3 _anchorWorldPos;
        private RectTransform _layerRect;
        private bool _loggedMissingLayer;

        // Bumped on every show/hide edge so a queued view operation can tell it has been superseded.
        private int _generation;

        // Serialises the view's own ShowAsync/HideAsync instead of cancelling them.
        //
        // UIViewBase.HideAsync deactivates the GameObject in BOTH its normal and its cancellation
        // path (UIViewBase.cs:87-101), and leaves IsVisible true for the whole in-flight hide. So
        // cancelling a hide to start a show — exactly what the re-show window provokes — used to
        // let the cancelled hide's tail deactivate a tooltip the service already considered shown,
        // stranding it invisible forever. Chaining removes that race by construction.
        private UniTask _viewOps = UniTask.CompletedTask;

        public bool IsShown => _state is State.Shown or State.Grace;
        public RectTransform CurrentAnchor => _anchor;
        public float LongPressSeconds => _longPressThreshold;
        public float LongPressMoveCancelPixels => _longPressMoveCancelPixels;

        [Inject]
        public TooltipService(UIRootLayerRefs layers, UIFrameworkConfig config,
            IObjectResolver resolver, ITransitionOverlay overlay)
        {
            _layers = layers;
            _overlay = overlay;
            _index = new TooltipViewIndex(resolver);

            _showDelay = config.TooltipShowDelaySeconds;
            _hideGrace = config.TooltipHideGraceSeconds;
            _reShowWindow = config.TooltipReShowWindowSeconds;
            _longPressThreshold = config.TooltipLongPressSeconds;
            _longPressMoveCancelPixels = config.TooltipLongPressMoveCancelPixels;
        }

        public void Initialize() => _index.DiscoverSceneViews();

        public void Register(TooltipViewBase view) => _index.Add(view);

        public void Show(in TooltipRequest request)
        {
            if (!request.IsValid) return;

            // Refuse while the loading curtain is up. The navigation hooks fire at transition
            // EDGES, but the curtain stays up for the whole load and never blocks lower
            // raycasters — without this, a hover during a load pops a tooltip over it with
            // nothing left to clear it.
            if (CurtainUp()) return;

            if (ResolveLayer() == null) return;

            _request = request;
            _anchor = request.Anchor;

            // Click and focus are deliberate acts where any delay reads as lag. Hover and touch
            // wait, unless a tooltip is already up or only just came down — that re-show window is
            // what makes sweeping across a grid feel responsive instead of sluggish.
            bool instant = request.Source is TooltipSource.Click or TooltipSource.Focus
                        || _state is State.Shown or State.Grace
                        || Time.unscaledTime - _lastShownAt <= _reShowWindow;

            if (instant)
            {
                ShowNow();
            }
            else
            {
                _state = State.Pending;
                _timer = _showDelay;
            }
        }

        public void Hide(RectTransform source)
        {
            // Stale exit: moving between two triggers can fire the old exit after the new enter,
            // so a naive hide would kill the tooltip that was just shown.
            if (source != null && _anchor != null && source != _anchor) return;

            switch (_state)
            {
                case State.Pending:
                    Reset();
                    break;
                case State.Shown:
                    _state = State.Grace;
                    _timer = _hideGrace;
                    break;
                // Already in Grace: deliberately a no-op. A pointer resting on the widget's edge
                // raises repeated exits, and re-arming the countdown on each would keep a dead
                // tooltip alive indefinitely.
            }
        }

        // "Immediate" means no grace period, not zero-frame: the hide transition still plays, so
        // UIViewBase's IsVisible/SetActive bookkeeping stays correct. Bypassing HideAsync to force
        // a same-frame hide would leave IsVisible stuck true and break the next show.
        public void HideImmediate()
        {
            // Revoked FIRST, before the early-out. A tooltip that hid through grace a moment ago
            // has already stamped its re-show window, and returning early here would leave that
            // window intact across the navigation — the next screen's first hover would then show
            // with zero dwell.
            _lastShownAt = float.NegativeInfinity;

            if (_state == State.Idle && _active == null) return;

            // No re-show window: this is a navigation or curtain teardown.
            HideNow(grantReShowWindow: false);
        }

        public void Tick()
        {
            switch (_state)
            {
                case State.Pending:
                    if (!AnchorAlive() || CurtainUp()) { Reset(); return; }
                    _timer -= Time.unscaledDeltaTime;
                    if (_timer <= 0f) ShowNow();
                    break;

                case State.Shown:
                    // A consumed item or a RecyclerView cell recycled out from under the pointer
                    // sends no OnPointerExit, so the anchor is re-validated every frame.
                    if (!AnchorAlive() || CurtainUp()) { HideNow(grantReShowWindow: false); return; }
                    if (_anchor.position != _anchorWorldPos) Reposition();
                    break;

                case State.Grace:
                    _timer -= Time.unscaledDeltaTime;
                    if (_timer <= 0f) HideNow(grantReShowWindow: true);
                    break;
            }
        }

        private void ShowNow()
        {
            var layer = ResolveLayer();
            var view = _index.Resolve(_request.Payload?.ViewKey);
            if (layer == null || view == null || !_request.IsValid)
            {
                // Must tear the current tooltip down, not just Reset: Reset leaves _active set and
                // drops the state to Idle, which stops the watchdog and would strand whatever is
                // on screen there permanently.
                HideNow(grantReShowWindow: false);
                return;
            }

            if (_active != null && _active != view)
            {
                var outgoing = _active;
                Enqueue(async () =>
                {
                    if (outgoing == null) return;   // destroyed while queued
                    await outgoing.HideAsync(CancellationToken.None);
                });
            }

            _active = view;
            _state = State.Shown;
            _lastShownAt = Time.unscaledTime;

            var request = _request;
            var anchor = _anchor;

            // Set synchronously as well as inside the op: an op that returns early as superseded
            // would otherwise leave this stale and make Tick reposition every single frame.
            _anchorWorldPos = anchor.position;

            // Supersession token. `_active != view` is not enough on its own — the common setup is
            // ONE shared tooltip view, so N rapid shows would all pass that check and each replay
            // a full bind/position/entrance for anchors the pointer left long ago.
            int generation = ++_generation;

            Enqueue(async () =>
            {
                // Re-checked at RUN time, not just at enqueue time: a queued op can be reached
                // after a teardown has already destroyed the view or the layer.
                if (generation != _generation || anchor == null || view == null || layer == null)
                    return;

                view.transform.SetParent(layer, false);
                view.transform.SetAsLastSibling();

                // Activate BEFORE binding and measuring. LayoutRebuilder strips disabled
                // behaviours, so a ContentSizeFitter on an inactive GameObject never runs and the
                // positioner would flip and clamp against the PREVIOUS payload's size.
                // UIViewBase.ShowAsync only activates at its :45, which is far too late for this.
                if (!view.gameObject.activeSelf)
                {
                    // Activating with the old alpha would flash stale content at a stale position
                    // for one frame before the show transition takes over.
                    if (view.CanvasGroup != null) view.CanvasGroup.alpha = 0f;
                    view.gameObject.SetActive(true);
                }

                view.Bind(request.Payload);
                TooltipPositioner.Position(view.RectTransform, anchor, layer, request.Placement);
                _anchorWorldPos = anchor.position;

                await view.ShowAsync(CancellationToken.None);
            });
        }

        private void Reposition()
        {
            var layer = ResolveLayer();
            // Skip while the show is still queued behind an animation — measuring an inactive
            // rect is exactly the bug the activate-first ordering above exists to avoid.
            if (layer == null || _active == null || !_active.gameObject.activeInHierarchy) return;

            TooltipPositioner.Position(_active.RectTransform, _anchor, layer, _request.Placement);
            _anchorWorldPos = _anchor.position;
        }

        private void HideNow(bool grantReShowWindow)
        {
            // Supersedes any queued show — without this, a show still sitting behind an animation
            // would run after the hide and resurrect the tooltip.
            _generation++;

            var outgoing = _active;
            if (outgoing != null)
            {
                Enqueue(async () =>
                {
                    if (outgoing == null) return;   // destroyed while queued
                    await outgoing.HideAsync(CancellationToken.None);
                });
            }

            // Stamping the clock here is what gives a genuine hide its instant re-show window.
            _lastShownAt = grantReShowWindow ? Time.unscaledTime : float.NegativeInfinity;
            _active = null;
            Reset();
        }

        private void Reset()
        {
            _state = State.Idle;
            _anchor = null;
            _timer = 0f;
            _request = default;
        }

        private void Enqueue(Func<UniTask> operation)
        {
            _viewOps = Run(_viewOps, operation);

            static async UniTask Run(UniTask previous, Func<UniTask> operation)
            {
                // A failed or cancelled predecessor must not stall the queue forever.
                try { await previous; } catch { }
                try { await operation(); }
                catch (OperationCanceledException) { }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private bool AnchorAlive() => _anchor != null && _anchor.gameObject.activeInHierarchy;

        private bool CurtainUp() => _overlay != null && _overlay.IsShown;

        private RectTransform ResolveLayer()
        {
            if (_layerRect != null) return _layerRect;

            var t = _layers?.GetLayer(UILayer.Tooltip);
            if (t == null)
            {
                // Pre-v1.7 UIRoots deserialise Tooltip as null (UIRootLayerRefs serialises by field
                // name), and SetLayerInteractable returns silently on a null transform — so without
                // this the whole feature fails invisibly.
                t = _layers?.GetLayer(UILayer.Overlay);
                if (!_loggedMissingLayer)
                {
                    _loggedMissingLayer = true;
                    Debug.LogError(
                        "[TooltipService] UIRoot has no Tooltip layer — falling back to Overlay. " +
                        "Run Tools/UIFramework/Upgrade UIRoot Layers to add it.");
                }
            }

            _layerRect = t as RectTransform;
            return _layerRect;
        }
    }
}
