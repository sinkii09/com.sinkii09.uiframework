using System.Collections.Generic;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Owns every live notification: which are visible, which are waiting, and when each expires.
    ///
    /// <para>THE ENTRY LIST IS THE SOURCE OF TRUTH and is advanced whether or not any view exists.
    /// Views are a rendering detail — a destroyed host, a missing prefab or an un-migrated UIRoot
    /// all degrade what is drawn, never whether the queue drains. That is what makes wedging
    /// structurally impossible rather than merely unlikely.</para>
    ///
    /// <para>Everything advances in <see cref="Tick"/> off unscaled time, including the fades. Two
    /// reasons, the same ones <c>TooltipService</c> gives: this project pauses with
    /// <c>timeScale = 0</c>, so a scaled wait would mean no toast ever appears in a pause menu; and
    /// a tick-driven machine is genuinely frame-testable, with no wall-clock in the tests. A third
    /// reason applies here specifically — with no async show/hide there is no operation to
    /// interleave, so the cancelled-tail hazard (a superseded hide deactivating a slot the next
    /// notification has already claimed, stranding an invisible toast in a live slot forever)
    /// cannot be expressed.</para>
    /// </summary>
    public sealed class NotificationService : INotificationService, IInitializable, ITickable, System.IDisposable
    {
        // A queue this deep already means something is spamming; the cap bounds memory and the
        // promotion scan. Not configurable: no consumer has needed it to be.
        internal const int MaxQueued = 32;

        // One scene-load hitch must not expire every toast at once.
        private const float MaxTickDelta = 0.1f;

        private enum SlotPhase { Free, FadingIn, Held, FadingOut }

        private sealed class Entry
        {
            internal NotificationKey Key;
            internal NotificationContent Content;
            internal int Sequence;      // insertion order; a merge NEVER renumbers it
            internal float Remaining;   // dismiss timer: reset by merge, paused behind the curtain
            internal float Lifetime;    // accumulated ticks: never reset, never paused
            internal int Slot = -1;     // -1 while waiting
        }

        private sealed class Slot
        {
            internal NotificationItemView View;
            internal Entry Entry;
            internal SlotPhase Phase;
            internal float Fade;        // 0..1
        }

        private readonly UIRootLayerRefs _layers;
        private readonly ITransitionOverlay _overlay;
        private readonly IObjectResolver _resolver;

        private readonly List<Entry> _entries = new();
        private readonly List<Slot> _slots = new();

        // internal so tests can zero them; AssemblyInfo grants both test assemblies access.
        internal float _defaultDuration;
        internal float _maxLifetime;
        internal float _fadeSeconds;
        internal int _maxVisible;

        private NotificationHostView _host;
        private RectTransform _layerRect;
        private bool _usingFallbackLayer;
        private bool _loggedMissingLayer;
        private int _sequence;
        private bool _disposed;

        public int ActiveCount => _entries.Count;

        // Test seams. ActiveCount alone cannot distinguish "dropped a waiter" from "dropped a
        // visible one" — both leave the same count — so the rules that talk about visibility need
        // a way to observe it.
        internal int VisibleCount
        {
            get
            {
                int n = 0;
                foreach (Entry e in _entries) if (e.Slot >= 0) n++;
                return n;
            }
        }

        internal bool IsBound(in NotificationKey key)
        {
            Entry e = Find(key);
            return e != null && e.Slot >= 0;
        }

        internal bool Contains(in NotificationKey key) => Find(key) != null;

        [Inject]
        public NotificationService(UIRootLayerRefs layers, UIFrameworkConfig config,
            IObjectResolver resolver, ITransitionOverlay overlay)
        {
            _layers = layers;
            _resolver = resolver;
            _overlay = overlay;

            _defaultDuration = config.NotificationDurationSeconds;
            _maxLifetime = config.NotificationMaxLifetimeSeconds;
            _fadeSeconds = config.NotificationFadeSeconds;

            // Clamped, because MaxVisible is user-editable and a value above MaxQueued would make
            // the overflow rule (drop the lowest-priority WAITING entry) unsatisfiable.
            _maxVisible = Mathf.Clamp(config.NotificationMaxVisible, 1, MaxQueued);
            if (config.NotificationMaxVisible != _maxVisible)
                Debug.LogWarning(
                    $"[NotificationService] NotificationMaxVisible {config.NotificationMaxVisible} " +
                    $"clamped to {_maxVisible} (must be between 1 and {MaxQueued}).");

            for (int i = 0; i < _maxVisible; i++) _slots.Add(new Slot());
        }

        public void Initialize() => DiscoverHost();

        public void Notify(in NotificationRequest request)
        {
            if (!request.Key.IsValid)
            {
                // Without this, every keyless notification shares one identity and merges into a
                // single toast whose quantity climbs forever.
                Debug.LogError("[NotificationService] Notify called with a blank NotificationKey — " +
                               "give every notification a Category and/or an Id. Request dropped.");
                return;
            }

            Entry existing = Find(request.Key);
            if (existing != null)
            {
                // Merge: quantity accumulates, priority only rises, the dismiss timer restarts —
                // but Sequence and Lifetime are untouched. Sequence because a merge is an update,
                // not a re-arrival; Lifetime because it is the guarantee of termination, and a
                // notification repeating every half second would otherwise never be dismissible.
                existing.Content = existing.Content.MergedWith(request.Content);
                existing.Remaining = DurationOf(existing.Content);
                if (existing.Slot >= 0)
                {
                    Slot slot = _slots[existing.Slot];
                    // A merge arriving mid-fade-out must bring the toast back, or the quantity it
                    // just accumulated is destroyed as the fade completes. Fade resumes from its
                    // partial value. This stays bounded only because Retire runs before AdvanceSlots
                    // every tick, so MaxLifetime still flips it straight back to FadingOut — that
                    // ordering is load-bearing, not incidental.
                    if (slot.Phase == SlotPhase.FadingOut) slot.Phase = SlotPhase.FadingIn;
                    slot.View?.Bind(existing.Content);
                }
                return;
            }

            if (_entries.Count >= MaxQueued && !DropLowestWaiter(request.Content.Priority))
                return;

            _entries.Add(new Entry
            {
                Key = request.Key,
                Content = request.Content,
                Sequence = _sequence++,
                Remaining = DurationOf(request.Content),
            });
        }

        public void Dismiss(in NotificationKey key)
        {
            Entry entry = Find(key);
            if (entry != null) Retire(entry);
        }

        public void DismissAll()
        {
            // Reverse: Retire removes waiting entries from the list outright.
            for (int i = _entries.Count - 1; i >= 0; i--) Retire(_entries[i]);
        }

        // The clamp lives here, on the untrusted value: one scene-load hitch must not expire every
        // toast at once. It deliberately does NOT live in the seam below — a caller passing an
        // explicit delta is not the frame clock, and silently capping it would make the seam lie
        // about what it applied.
        public void Tick() => Tick(Mathf.Min(Time.unscaledDeltaTime, MaxTickDelta));

        // The real seam. Public Tick() feeds it the (clamped) frame delta; tests feed it an exact
        // one, which is what lets a duration test assert "alive before, gone after" instead of
        // zeroing every timer and passing whatever the implementation happens to do.
        internal void Tick(float dt)
        {
            if (_entries.Count == 0 && !AnySlotBusy()) return;

            // The dismiss timer pauses behind the loading curtain, because a toast on the
            // Notification layer sits UNDER it and would otherwise expire unseen. It does not pause
            // on the fallback layer, where the toast draws over the curtain and is plainly visible.
            bool hideBehindCurtain = CurtainUp() && !_usingFallbackLayer;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry e = _entries[i];

                // Lifetime is never paused: it is the guarantee that an entry terminates. Pausing
                // it would let an overlay that never hides make every entry immortal, and at
                // MaxQueued every later Notify would be dropped forever.
                e.Lifetime += dt;

                // Lifetime accrues while WAITING and behind the curtain too, so a long curtain or a
                // long saturation eventually discards the waiting queue rather than growing it.
                // Only a VISIBLE entry burns its dismiss timer. A waiting one that counted down
                // would expire unseen while saturated — which would make priority ordering
                // decorative, since a queued Error could die before a slot ever freed.
                if (e.Slot >= 0 && !hideBehindCurtain) e.Remaining -= dt;

                if (e.Remaining <= 0f || e.Lifetime >= _maxLifetime) Retire(e);
            }

            AdvanceSlots(dt);

            // Promotion happens ONCE per tick and is computed from the entry list, never from a
            // fade continuation and never from "which slots look free". Two slots finishing a fade
            // in the same frame would otherwise each promote the same waiter.
            PromoteWaiters();

            // AFTER promotion, deliberately. Hiding at the end of AdvanceSlots instead would toggle
            // the host off and straight back on whenever a slot releases and a waiter is promoted in
            // the same tick — the normal drain path — running OnDisable/OnEnable across every row.
            if (!AnySlotBusy()) HideHost();
        }

        private void AdvanceSlots(float dt)
        {
            float step = _fadeSeconds > 0f ? dt / _fadeSeconds : 1f;

            foreach (Slot slot in _slots)
            {
                switch (slot.Phase)
                {
                    case SlotPhase.FadingIn:
                        slot.Fade = Mathf.Min(1f, slot.Fade + step);
                        slot.View?.SetAlpha(slot.Fade);
                        if (slot.Fade >= 1f) slot.Phase = SlotPhase.Held;
                        break;

                    case SlotPhase.FadingOut:
                        slot.Fade = Mathf.Max(0f, slot.Fade - step);
                        slot.View?.SetAlpha(slot.Fade);
                        // The slot frees only once the fade completes, so two toasts never overlap
                        // in one row. Promotion therefore lags expiry by the fade duration.
                        if (slot.Fade <= 0f) ReleaseSlot(slot);
                        break;
                }
            }

        }

        private void PromoteWaiters()
        {
            bool shownHost = false;
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].Phase != SlotPhase.Free) continue;

                Entry best = BestWaiter();
                if (best == null) return;

                best.Slot = i;
                Slot slot = _slots[i];
                slot.Entry = best;
                slot.Phase = SlotPhase.FadingIn;
                slot.Fade = 0f;

                // Host first, and in this order for a concrete reason: a row instantiated under an
                // INACTIVE parent never runs Awake, so UIControlBase.OnInitialize never fires, its
                // CanvasGroup stays null, and every SetAlpha silently no-ops — the fade machine
                // would drive nothing and the feature would be invisible without ever throwing.
                // Once per tick, not once per slot: it can cost a scene-wide find.
                if (!shownHost) { ShowHost(); shownHost = true; }

                slot.View = _host != null ? _host.GetOrCreateItem(i) : null;
                if (slot.View != null)
                {
                    slot.View.Bind(best.Content);
                    slot.View.SetAlpha(0f);
                    // Activated last: a row's OnEnable can run game code that calls back into this
                    // service, and the slot must already be fully consistent when it does.
                    slot.View.SetActive(true);
                }
            }
        }

        // Highest priority, then oldest. A merge that raises a waiter's priority can win here, but
        // it still cannot displace anything already visible — visibility is sticky.
        private Entry BestWaiter()
        {
            Entry best = null;
            foreach (Entry e in _entries)
            {
                if (e.Slot >= 0) continue;
                if (best == null
                    || e.Content.Priority > best.Content.Priority
                    || (e.Content.Priority == best.Content.Priority && e.Sequence < best.Sequence))
                    best = e;
            }
            return best;
        }

        // Over-cap: drop the lowest-priority, oldest WAITING entry — never a visible one, which
        // would vanish mid-read. Returns false when nothing can be freed, in which case the
        // incoming request is refused instead. Only reachable when MaxVisible == MaxQueued.
        private bool DropLowestWaiter(NotificationPriority incoming)
        {
            Entry worst = null;
            foreach (Entry e in _entries)
            {
                if (e.Slot >= 0) continue;
                if (worst == null
                    || e.Content.Priority < worst.Content.Priority
                    || (e.Content.Priority == worst.Content.Priority && e.Sequence < worst.Sequence))
                    worst = e;
            }

            if (worst == null)
            {
                Debug.LogWarning($"[NotificationService] Queue full ({MaxQueued}) and every entry is " +
                                 $"visible — incoming {incoming} notification dropped.");
                return false;
            }

            // Evict only something the arrival actually outranks. Without this a Normal spam
            // notification would push a queued Error out of the list.
            if (worst.Content.Priority >= incoming)
            {
                Debug.LogWarning($"[NotificationService] Queue full ({MaxQueued}) — incoming {incoming} " +
                                 "notification dropped; every waiting entry ranks at least as high.");
                return false;
            }

            Debug.LogWarning($"[NotificationService] Queue full ({MaxQueued}) — dropped waiting " +
                             $"notification '{worst.Key}' to make room.");
            _entries.Remove(worst);
            return true;
        }

        // Begins a bound entry's fade-out, or removes a waiting one outright. Idempotent: an entry
        // already fading out is left alone so a second Dismiss cannot restart or double-remove it.
        private void Retire(Entry entry)
        {
            if (entry.Slot < 0)
            {
                _entries.Remove(entry);
                return;
            }

            Slot slot = _slots[entry.Slot];
            if (slot.Phase == SlotPhase.FadingOut) return;
            slot.Phase = SlotPhase.FadingOut;
        }

        private void ReleaseSlot(Slot slot)
        {
            if (slot.Entry != null)
            {
                slot.Entry.Slot = -1;
                _entries.Remove(slot.Entry);
                slot.Entry = null;
            }
            slot.Phase = SlotPhase.Free;
            slot.Fade = 0f;
            slot.View?.SetActive(false);
        }

        private bool AnySlotBusy()
        {
            foreach (Slot slot in _slots)
                if (slot.Phase != SlotPhase.Free) return true;
            return false;
        }

        private Entry Find(in NotificationKey key)
        {
            foreach (Entry e in _entries)
                if (e.Key.Equals(key)) return e;
            return null;
        }

        private float DurationOf(in NotificationContent content)
            => content.DurationSeconds > 0f ? content.DurationSeconds : _defaultDuration;

        private bool CurtainUp() => _overlay != null && _overlay.IsShown;

        // The host is pre-hidden in Awake so the first frame after a scene load never flashes an
        // empty container; nothing else would ever bring it back. Deliberately NOT UIViewBase's
        // ShowAsync: that would add a transition and re-enable raycasts on a host that must stay
        // non-blocking. Rows carry their own fades, so the host only needs to exist.
        private void ShowHost()
        {
            if (_disposed) return;

            // A scene reload destroys a non-persistent host. Re-discovery is attempted only here —
            // when something actually needs rendering — so the cost is per-burst, not per-frame.
            if (_host == null) DiscoverHost();
            if (_host == null) return;

            if (_host.CanvasGroup != null) _host.CanvasGroup.alpha = 1f;
            if (!_host.gameObject.activeSelf) _host.gameObject.SetActive(true);
        }

        private void HideHost()
        {
            if (_host == null || !_host.gameObject.activeSelf) return;
            if (_host.CanvasGroup != null) _host.CanvasGroup.alpha = 0f;
            _host.gameObject.SetActive(false);
        }

        private void DiscoverHost()
        {
            var host = Object.FindAnyObjectByType<NotificationHostView>(FindObjectsInactive.Include);
            if (host == null) return;
            AttachHost(host);
        }

        private void AttachHost(NotificationHostView host)
        {
            _host = host;
            _host.Destroyed += OnHostDestroyed;

            // Mandatory: resident scene views are not otherwise injected, and a view with a null
            // animator has its UITransitions silently never play.
            _resolver?.InjectGameObject(host.gameObject);

            RectTransform layer = ResolveLayer();
            if (layer != null) host.transform.SetParent(layer, worldPositionStays: false);
        }

        // Every bound slot drops its view. Entries are deliberately untouched: they keep ticking
        // and expire on schedule, so losing the host cannot wedge the queue.
        private void OnHostDestroyed()
        {
            if (_host != null) _host.Destroyed -= OnHostDestroyed;
            _host = null;
            foreach (Slot slot in _slots) slot.View = null;
        }

        // VContainer disposes singleton entry points. Without this the Destroyed handler keeps the
        // service reachable from a host that outlives the scope.
        public void Dispose()
        {
            _disposed = true;
            if (_host != null) _host.Destroyed -= OnHostDestroyed;
            _host = null;
        }

        internal RectTransform ResolveLayer()
        {
            if (_layerRect != null) return _layerRect;

            // Cleared before every (re-)resolve: after a scene reload the replacement UIRoot may
            // have the layer this one lacked, and a stale latch would keep toasts counting down
            // behind the curtain as though they were still on the fallback.
            _usingFallbackLayer = false;

            var t = _layers?.GetLayer(UILayer.Notification);
            if (t == null)
            {
                // UIRootLayerRefs serialises by field name, so a pre-v2.2 UIRoot deserialises
                // Notification as null — and SetLayerInteractable returns silently on a null
                // transform, so without this the whole feature would fail invisibly.
                _usingFallbackLayer = true;
                t = _layers?.GetLayer(UILayer.Overlay);
                if (!_loggedMissingLayer)
                {
                    _loggedMissingLayer = true;
                    Debug.LogError(
                        "[NotificationService] UIRoot has no Notification layer — falling back to " +
                        "Overlay, where toasts will draw over the loading curtain. " +
                        "Run Tools/UIFramework/Upgrade UIRoot Layers to add it.");
                }
            }

            _layerRect = t as RectTransform;
            return _layerRect;
        }
    }
}
