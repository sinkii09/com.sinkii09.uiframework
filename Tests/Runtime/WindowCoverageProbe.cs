using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests
{
    /// <summary>
    /// Checks, once per frame and after every other LateUpdate — ScrollRect's inertia step included —
    /// whether the shown cells cover the viewport. That is the state the frame is rendered with; a
    /// check from a coroutine or Update would run before the ScrollRect moves and see nothing wrong.
    ///
    /// <para>Vertical, TopToBottom, uniform stride only: the viewport's offset-space start is then the
    /// content's anchored y, and item <c>i</c> starts at <c>i * stride</c>.</para>
    /// </summary>
    [DefaultExecutionOrder(32000)]
    internal class WindowCoverageProbe : MonoBehaviour
    {
        private RecyclerView _view;
        private RectTransform _content;
        private float _viewportSize;
        private float _stride;

        public int Frames { get; private set; }
        public int UncoveredFrames { get; private set; }

        public void Watch(RecyclerView view, RectTransform content, float viewportSize, float stride)
        {
            _view = view;
            _content = content;
            _viewportSize = viewportSize;
            _stride = stride;
        }

        private void LateUpdate()
        {
            if (_view == null) return;
            Frames++;

            IReadOnlyList<int> shown = _view.ShownIndices;
            float start = _content.anchoredPosition.y;
            float end = start + _viewportSize;

            bool covered = shown.Count > 0
                           && shown[0] * _stride <= start
                           && (shown[shown.Count - 1] + 1) * _stride >= end;
            if (!covered) UncoveredFrames++;
        }
    }
}
