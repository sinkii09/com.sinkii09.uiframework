using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sinkii09.UIFramework
{
    public class Draggable : UIControlBase, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private readonly Subject<Vector2> _onBeginDrag = new();
        private readonly Subject<Vector2> _onDrag = new();
        private readonly Subject<Vector2> _onEndDrag = new();

        public Observable<Vector2> OnBeginDragAsObservable => _onBeginDrag;
        public Observable<Vector2> OnDragAsObservable => _onDrag;
        public Observable<Vector2> OnEndDragAsObservable => _onEndDrag;

        private RectTransform _rect;
        private Canvas _canvas;

        protected override void OnInitialize()
        {
            _rect = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
        }

        protected override void OnDispose()
        {
            _onBeginDrag.Dispose();
            _onDrag.Dispose();
            _onEndDrag.Dispose();
        }

        public void OnBeginDrag(PointerEventData evt)
        {
            if (!Interactable) return;
            _onBeginDrag.OnNext(evt.position);
        }

        public void OnDrag(PointerEventData evt)
        {
            if (!Interactable) return;
            if (_canvas != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvas.transform as RectTransform, evt.position,
                    evt.pressEventCamera, out var local))
                _rect.localPosition = local;

            _onDrag.OnNext(evt.position);
        }

        public void OnEndDrag(PointerEventData evt)
        {
            if (!Interactable) return;
            _onEndDrag.OnNext(evt.position);
        }
    }
}
