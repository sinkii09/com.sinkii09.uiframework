using R3;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UIControlBase : MonoBehaviour
    {
        private CanvasGroup _group;
        protected DisposableBag _disposables;

        public bool Interactable
        {
            get => _group != null && _group.interactable;
            set { if (_group != null) _group.interactable = value; }
        }

        protected abstract void OnInitialize();
        protected abstract void OnDispose();

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            OnInitialize();
        }

        private void OnDestroy()
        {
            OnDispose();            // owned objects (Subjects, ReactiveProperties) first
            _disposables.Dispose(); // subscription teardown second
        }
    }
}
