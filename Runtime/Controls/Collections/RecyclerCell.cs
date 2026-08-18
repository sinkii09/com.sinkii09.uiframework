using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Base for a recycled cell in a <see cref="RecyclerView"/>.
    ///
    /// Deliberately a plain <see cref="MonoBehaviour"/> and not a <c>UIControlBase</c>: cells are
    /// created and rebound constantly, so they carry no CanvasGroup and take no DI. Subclasses add
    /// their own <c>Bind(data)</c> method — the view never calls it, the cell provider does.
    /// </summary>
    public abstract class RecyclerCell : MonoBehaviour
    {
        /// <summary>Data index this cell currently represents. Owned by the view.</summary>
        public int Index { get; internal set; } = -1;

        /// <summary>
        /// Called just before the cell is returned to the pool. Release anything that would
        /// otherwise leak across a rebind — subscriptions, tweens, loaded sprites.
        /// </summary>
        public virtual void OnRecycled() { }
    }
}
