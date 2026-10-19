using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Plays one Animator state once, on demand, and keeps the Animator OFF the rest of the time — for
    /// reveal flourishes (shine sweeps, stamps, pops) authored as AnimationClips.
    /// </summary>
    /// <remarks>
    /// An enabled Animator rewrites every bound property each frame even when nothing changes; on UI that
    /// dirties the graphic (UIEffect rebuilds its mesh and material) — a steady per-frame cost for a grid of
    /// items. So the Animator only runs while a clip plays.
    /// <list type="bullet">
    /// <item>A run ends when its state reaches normalizedTime 1, the Animator moves to another state, or
    /// something else disables the Animator. A looping state therefore stops after its first cycle, resting
    /// on its loop pose; a state whose speed is 0 or less never ends on its own — call <see cref="Complete"/>.</item>
    /// <item>Awake forces <c>cullingMode</c> to AlwaysAnimate: UI has no Renderer, and a culled Animator may
    /// never advance, so the run would never end.</item>
    /// <item>Disabled mid-run (a recycled list cell), it comes back on the clip's end pose rather than frozen
    /// half-way — a disabled Animator keeps whatever it last wrote.</item>
    /// </list>
    /// </remarks>
    [RequireComponent(typeof(Animator))]
    [DisallowMultipleComponent]
    public sealed partial class OneShotAnimator : MonoBehaviour
    {
        [Header("Clip")]
        [Tooltip("Animator state Play() runs once. Must exist on the layer below.")]
        [SerializeField] private string _state = "Play";
        [Tooltip("Animator layer index the state lives on.")]
        [SerializeField, Min(0)] private int _layer;

        [Header("Playback")]
        [Tooltip("Play every time this object is enabled. Leave off for cells a list recycles.")]
        [SerializeField] private bool _playOnEnable;
        [Tooltip("Animate on unscaled time, so it still plays while the game is paused.")]
        [SerializeField] private bool _unscaledTime = true;

        private Animator _animator;
        private UniTaskCompletionSource _run;   // null when idle; one per Play, completes every awaiter
        private int _runHash;
        private string _runState;
        private bool _playing;
        private bool _pendingPlay;
        private bool _interrupted;
        private bool _awoken;
        private bool _destroyed;
        private bool _watchingDestroy;
        private bool _reportedError;   // Play may fire per cell per scroll — report a broken setup once

        /// <summary>True while a run plays, or waits for this object to be enabled.</summary>
        public bool IsPlaying => _playing || _pendingPlay;

        private Animator CachedAnimator => _animator != null ? _animator : _animator = GetComponent<Animator>();

        private void Awake()
        {
            _awoken = true;
            Animator animator = CachedAnimator;
            animator.enabled = false;   // off at rest even when a prefab ships it enabled
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (_unscaledTime) animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }

        private void OnEnable()
        {
            if (_pendingPlay) StartPending();
            else if (_playOnEnable) Play();
            else if (_interrupted) SnapToEnd();
        }

        private void OnDisable()
        {
            if (!_playing) return;   // a pending run stays pending across a disable

            _playing = false;
            _interrupted = true;
            CachedAnimator.enabled = false;
            EndRun();
        }

        private void OnDestroy()
        {
            _destroyed = true;
            _pendingPlay = false;
            EndRun();
        }

        /// <summary>Plays the configured state from the start; mid-run, restarts it.</summary>
        public void Play() => BeginRun(_state);

        /// <summary>
        /// Plays <paramref name="state"/> from the start. Called while this object is inactive or this
        /// component is disabled, it plays as soon as it is enabled again.
        /// </summary>
        public void Play(string state) => BeginRun(state);

        /// <summary>
        /// Plays the configured state and completes when that run ends: it finished, was restarted by
        /// another Play, was cut short by a disable or <see cref="Complete"/>, or this object was destroyed.
        /// Cancelling <paramref name="ct"/> only abandons the wait — the animation keeps playing; a token that
        /// is already cancelled returns a cancelled task and nothing plays.
        /// </summary>
        /// <remarks>
        /// Awaited on an object that has never been active, the run waits for it to be enabled; if the
        /// GameObject is destroyed first, a destroy trigger (an <c>AsyncDestroyTrigger</c> component UniTask
        /// adds to it) ends the run. Destroying only this component there ends nothing — pass a token.
        /// </remarks>
        public UniTask PlayAsync(CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return UniTask.FromCanceled(ct);
            return BeginRun(_state).Task.AttachExternalCancellation(ct);
        }

        /// <summary>Jumps the current run to its end pose and stops (e.g. a "skip" tap). No-op when idle.</summary>
        public void Complete()
        {
            if (_pendingPlay)
            {
                _pendingPlay = false;
                _interrupted = true;   // snaps to the end pose when enabled
                EndRun();
                return;
            }
            if (!_playing) return;

            _playing = false;
            SnapToEnd();
            EndRun();
        }

        private void LateUpdate()
        {
            if (!_playing) return;

            // LateUpdate runs after the Animator evaluated this frame, so the final pose is already applied.
            Animator animator = CachedAnimator;
            if (animator.enabled)
            {
                AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(_layer);
                bool sameState = info.shortNameHash == _runHash || info.fullPathHash == _runHash;
                if (sameState && info.normalizedTime < 1f) return;
            }

            // Finished, moved to another state, or something else turned the Animator off.
            _playing = false;
            animator.enabled = false;
            EndRun();
        }
    }
}
