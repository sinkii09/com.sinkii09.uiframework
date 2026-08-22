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
        /// Size along the scroll axis this cell was <b>declared</b> to have — from the size provider,
        /// or the uniform setting when there is none. Never a measurement: the view decides the size
        /// and writes it to the cell, not the other way round. A cell whose real rect disagrees
        /// self-sized, and is reported as an error rather than silently absorbed.
        /// </summary>
        public float DeclaredSize;

        /// <summary>
        /// Pump tick on which this cell was created. Cells created on the current tick are exempt
        /// from recycling — without this the create/recycle loop can thrash a cell in and out
        /// within a single frame and never converge.
        /// </summary>
        public int CreatedTick;

        /// <summary>End of this cell in offset space, exclusive.</summary>
        public float EndOffset => Offset + DeclaredSize;
    }
}
