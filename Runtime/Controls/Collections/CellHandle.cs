using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// The view's bookkeeping for one currently-shown cell. Kept separate from
    /// <see cref="RecyclerCell"/> so consumer cell scripts stay free of engine fields.
    /// </summary>
    internal class CellHandle
    {
        public RecyclerCell Cell;
        public RectTransform Rect;

        /// <summary>Data index this cell is bound to.</summary>
        public int Index;

        /// <summary>Which pool this cell must be returned to.</summary>
        public int PrefabId;

        /// <summary>Start of this cell in offset space.</summary>
        public float Offset;

        /// <summary>
        /// Size along the scroll axis last measured from the cell's rect. In Phase 1 this should
        /// always equal the declared cell size; a mismatch means the cell self-sized and is
        /// reported as an error rather than silently absorbed.
        /// </summary>
        public float MeasuredSize;

        /// <summary>
        /// Pump tick on which this cell was created. Cells created on the current tick are exempt
        /// from recycling — without this the create/recycle loop can thrash a cell in and out
        /// within a single frame and never converge.
        /// </summary>
        public int CreatedTick;

        /// <summary>End of this cell in offset space, exclusive.</summary>
        public float EndOffset => Offset + MeasuredSize;
    }
}
