using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sinkii09.UIFramework
{
    public class DropZone : UIControlBase, IDropHandler
    {
        private readonly Subject<GameObject> _onDrop = new();
        public Observable<GameObject> OnDropAsObservable => _onDrop;

        protected override void OnInitialize() { }
        protected override void OnDispose() => _onDrop.Dispose();

        public void OnDrop(PointerEventData evt)
        {
            if (evt.pointerDrag != null)
                _onDrop.OnNext(evt.pointerDrag);
        }
    }
}
