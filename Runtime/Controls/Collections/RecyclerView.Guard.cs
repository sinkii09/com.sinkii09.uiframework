using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Re-entrancy guard. Every window in which consumer code runs — the cell provider,
    /// <see cref="RecyclerCell.OnRecycled"/>, the size provider, a <c>ForEachShownCell</c> action, a
    /// prefetch handler — is
    /// a busy window, and a mutating call made from inside one is refused rather than allowed to
    /// rewrite the shown-cell window while it is being walked.
    ///
    /// <para><b>One counter, not a flag per path.</b> The two flags this replaced each covered one
    /// path and left the rest open: <c>RefreshIndex</c> ran the provider with no guard at all, every
    /// public mutator ran <c>OnRecycled</c> unguarded, and <c>ForEachShownCell</c> cleared the pump's
    /// flag on exit even when it had been called from inside the pump. A nested mutation through any
    /// of those corrupted <c>_shown</c> and could hand a pooled <see cref="CellHandle"/> to two slots.</para>
    ///
    /// <para>Public methods open one scope around their whole body, including the pump that follows,
    /// and call private cores inside it. <c>Update</c>'s pump is the only caller that checks first and
    /// skips — anything else reaching a public method while busy is a consumer calling back in.</para>
    /// </summary>
    public partial class RecyclerView
    {
        private const string CellCallbacks = "a cell provider or OnRecycled";
        private const string SizeCallbacks = "a size provider";
        private const string VisitCallbacks = "a ForEachShownCell action";
        private const string PrefetchCallbacks = "a prefetch handler";

        private int _busyDepth;
        private string _busySource;

        private bool IsBusy => _busyDepth > 0;

        /// <summary>
        /// Opens a busy window. Always pair with <see cref="ExitBusy"/> in a <c>finally</c>, passing
        /// back the returned value — scopes nest, and the refusal message has to name the innermost.
        /// </summary>
        private string EnterBusy(string source)
        {
            string previous = _busySource;
            _busyDepth++;
            _busySource = source;
            return previous;
        }

        private void ExitBusy(string previousSource)
        {
            _busyDepth--;
            _busySource = previousSource;
        }

        private bool RejectReentrant(string caller)
        {
            if (!IsBusy) return false;

            Debug.LogError(
                $"[RecyclerView] '{name}': {caller} was called from inside {_busySource}. " +
                "Mutating the list while it is being built is not supported — the call was ignored.",
                this);
            return true;
        }
    }
}
