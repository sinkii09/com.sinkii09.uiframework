using System.Threading;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Base for a recycled cell in a <see cref="RecyclerView"/>.
    ///
    /// Deliberately a plain <see cref="MonoBehaviour"/> and not a <c>UIControlBase</c>: cells are
    /// created and rebound constantly, so they carry no CanvasGroup and take no DI. Subclasses add
    /// their own <c>Bind(data)</c> method — the view never calls it, the cell provider does.
    ///
    /// <para><b>Binding asynchronous content</b> (a sprite from Addressables, an avatar from the web):
    /// <list type="number">
    /// <item>Reset the visuals <b>synchronously</b> in Bind — show a placeholder. A cell recycled off
    /// one end is often re-bound at the other in the same frame, still on screen with the previous
    /// item's picture.</item>
    /// <item>Start the load with <see cref="BindCancellationToken"/>, and drop a result whose token
    /// was cancelled: it belongs to an item this cell no longer shows.</item>
    /// <item>Check a synchronous cache first, so a picture already in memory never flashes the
    /// placeholder.</item>
    /// </list>
    /// Never check a late result against <see cref="Index"/>: indices shift when the data does, so
    /// the same index can mean a different item.</para>
    /// </summary>
    public abstract class RecyclerCell : MonoBehaviour
    {
        private CancellationTokenSource _bindCancellation;
        private bool _recycling;

        /// <summary>
        /// Data index this cell currently represents. Owned by the view. Valid from the moment
        /// <c>RentCell</c> returns — inside the provider and the cell's own Bind — and <c>-1</c> while
        /// the cell is pooled.
        /// </summary>
        public int Index { get; internal set; } = -1;

        /// <summary>
        /// Cancelled the moment this cell stops representing its current item: recycled (scrolled
        /// out, refreshed, count changed), or destroyed. Pass it to any async work started for this
        /// bind and the "load finished after the cell moved on" race closes by itself.
        ///
        /// <para>Already cancelled outside a bind and inside <see cref="OnRecycled"/>. Created on
        /// first read, one per bind, so a cell that never reads it allocates nothing.</para>
        ///
        /// <para>Main thread only: read it in Bind and pass the captured value along. Reading the
        /// property from a background continuation can race the recycle that disposes its source.</para>
        /// </summary>
        public CancellationToken BindCancellationToken
        {
            get
            {
                // `this == null` before destroyCancellationToken: reading that on a destroyed object throws.
                if (Index < 0 || _recycling || this == null) return new CancellationToken(true);

                _bindCancellation ??= CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
                return _bindCancellation.Token;
            }
        }

        /// <summary>
        /// Called just before the cell is returned to the pool. Release anything that would
        /// otherwise leak across a rebind — subscriptions, tweens, loaded sprites. By the time it
        /// runs, <see cref="BindCancellationToken"/> has already been cancelled.
        ///
        /// <para>Must be idempotent: when a provider throws while re-binding this cell, it can be
        /// recycled twice with no bind in between. An exception thrown here is logged and the cell is
        /// pooled regardless. Calling back into the view's mutating methods from here is refused.</para>
        /// </summary>
        public virtual void OnRecycled() { }

        /// <summary>Starts a bind. Called by <see cref="RecyclerView.RentCell{T}"/>.</summary>
        internal void BeginBind(int index)
        {
            _recycling = false;
            Index = index;
        }

        /// <summary>
        /// Ends the current bind: cancels its token. Called by the pool before <see cref="OnRecycled"/>.
        /// Continuations awaiting the token can run synchronously inside <c>Cancel</c> — they run
        /// within the view's busy window, so any mutation they attempt is refused.
        /// </summary>
        internal void EndBind()
        {
            _recycling = true;

            CancellationTokenSource source = _bindCancellation;
            if (source == null) return;

            // Cleared first: a continuation reading the token during Cancel must see "recycling",
            // never a disposed source.
            _bindCancellation = null;
            try
            {
                source.Cancel();
            }
            finally
            {
                // Disposing unregisters the link from destroyCancellationToken; skipping it would
                // pile one registration per bind onto a cell that lives for the whole list.
                source.Dispose();
            }
        }
    }
}
