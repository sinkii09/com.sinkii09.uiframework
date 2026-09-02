using System;
using R3;

namespace Sinkii09.UIFramework
{
    // Owns the frame clock that coalesced UI bindings flush on.
    //
    // Exposed as an R3 FrameProvider rather than a bespoke callback list so that any R3 frame
    // operator a game already knows — DebounceFrame, ChunkFrame, ObserveOn — can be pointed at the
    // UI's own clock and behave consistently with the framework's bindings.
    //
    // THREADING: binding SOURCES must emit on the main thread. A binding applies its first value
    // inline on the calling thread and touches Unity API, so an off-main-thread emission is an
    // immediate violation. This constrains binding sources only — foreign work items registered on
    // Frames (ObserveOn's whole purpose is marshalling from a background thread) are safe, because
    // the registration list itself is gated. Suspend/Resume are also safe from any thread.
    public interface IUIRenderScheduler
    {
        // The frame clock. Pass to any R3 frame operator to have it flush on the UI's schedule.
        FrameProvider Frames { get; }

        // True while at least one Suspend() handle is outstanding.
        bool IsSuspended { get; }

        // Host frames elapsed in the current suspension episode; 0 when not suspended. Exposed so a
        // game can implement its own liveness policy — this class only reports, never force-resumes.
        int SuspendedFrames { get; }

        /// <summary>
        /// Stops flushing coalesced bindings until every returned handle is disposed. Refcounted,
        /// so overlapping suspensions compose.
        /// </summary>
        /// <remarks>
        /// While suspended each binding holds its newest value and applies it exactly once on
        /// resume; intermediate values are dropped. Intended for a bulk simulation window (offline
        /// catch-up, a fast-forward) where painting every intermediate state is wasted work.
        ///
        /// <para><b>Bindings are bounded under suspension; foreign R3 operators are NOT.</b> A
        /// coalesced binding stores one pending value regardless of how many arrive. An operator
        /// registered on <see cref="Frames"/> keeps its own semantics — notably
        /// <c>ObserveOn(Frames)</c> buffers EVERY value and drains them all on resume, so a long
        /// suspension queues one item per emission and floods when it ends. Use
        /// <c>UIBindMode.Immediate</c> or a different FrameProvider for those.</para>
        ///
        /// <para><b>This framework deliberately never calls this itself.</b> It is not wired into
        /// scene loads or transitions: the scene load happens inside <c>LoadingState</c> while that
        /// state's own view is on screen, and the transition curtain may be
        /// <c>NullTransitionOverlay</c> (which draws nothing) or semi-transparent — so the framework
        /// cannot know that suspending is safe. That call belongs to game code, which does.</para>
        ///
        /// <para><b>EXPERIMENTAL in v3.0.0.</b> Shipped with no in-tree consumer yet; the shape may
        /// change in a minor release rather than waiting for the next major.</para>
        /// </remarks>
        IDisposable Suspend();
    }
}
