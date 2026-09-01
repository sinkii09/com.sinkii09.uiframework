namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Outcome of a navigation request.
    ///
    /// <para>Navigation guards refuse requests that arrive while another transition is in flight,
    /// and the navigation stack itself declines a push past its depth limit or a pop on an empty
    /// stack. All of those used to return a <c>UniTask</c> that completed normally, so an awaiting
    /// caller could not tell "the view is now on screen" from "your request was thrown away" — and
    /// would go on to update its own state as though navigation had happened. Every guarded entry
    /// point now says which of the two occurred.</para>
    ///
    /// <para>A refusal is a normal, expected outcome, not an error: it is how the framework protects
    /// a transition already in progress. Callers that genuinely need the navigation to happen should
    /// react to <see cref="Rejected"/> (retry, or drive the request through
    /// <c>GameLifecycleManager</c>) rather than assume success.</para>
    ///
    /// <para>Cancellation is NOT represented here. An operation cancelled in flight still throws
    /// <see cref="System.OperationCanceledException"/>, exactly as before.</para>
    /// </summary>
    public enum NavigationResult
    {
        /// <summary>The operation ran to completion.</summary>
        Completed,

        /// <summary>
        /// Nothing happened — a guard refused the request, or the navigation stack declined it.
        /// The accompanying console warning says which; the distinction is not modelled here
        /// because no caller has needed to branch on it.
        /// </summary>
        Rejected
    }
}
