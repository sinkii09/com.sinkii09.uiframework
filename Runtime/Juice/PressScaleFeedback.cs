using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Squashes on pointer-down and springs back with an overshoot on release — the "anticipation +
    /// settle" pair that makes a button feel physical. Drop it next to a <see cref="Selectable"/>
    /// whose Transition is None or Color.
    /// </summary>
    /// <remarks>
    /// One scale driver per transform: if a pop-in or transition also scales this object, point
    /// <c>_target</c> at an inner visual child instead. Runs in unscaled time so it still answers
    /// in a pause menu. Scale only — LayoutGroup-safe.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PressScaleFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private Transform _target;
        [SerializeField] private Selectable _selectable;
        [SerializeField, Range(0.5f, 1f)] private float _pressedScale = 0.92f;
        [SerializeField] private float _pressDuration = 0.08f;
        [SerializeField] private float _releaseDuration = 0.25f;
        [SerializeField] private float _releaseOvershoot = 3f;

        // Pointer IDs currently holding the press. Release only when the LAST one lifts (multitouch),
        // and a hover-exit that never pressed is not in the set, so it cannot trigger a release.
        private readonly HashSet<int> _pressingPointers = new();
        private Vector3 _baseScale = Vector3.one;

        public bool IsPressed => _pressingPointers.Count > 0;
        public bool IsAnimating => _target != null && DOTween.IsTweening(_target);

        private void Awake()
        {
            if (_target == null) _target = transform;
            if (_selectable == null) _selectable = GetComponent<Selectable>();
            _baseScale = JuiceScale.CaptureBase(_target, this);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Selectable.interactable does not stop the EventSystem delivering pointer events to
            // sibling handlers — without this a greyed-out button would still squash.
            if (_selectable != null && !_selectable.IsInteractable()) return;

            bool alreadyPressed = IsPressed;
            _pressingPointers.Add(eventData.pointerId);
            if (alreadyPressed) return;

            KillAndRestore();
            _target.DOScale(_baseScale * _pressedScale, _pressDuration)
                .SetEase(Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        public void OnPointerUp(PointerEventData eventData) => Release(eventData.pointerId);

        public void OnPointerExit(PointerEventData eventData) => Release(eventData.pointerId);

        private void Release(int pointerId)
        {
            if (!_pressingPointers.Remove(pointerId) || IsPressed) return;

            // A bare kill is safe only here: the next tween writes the base scale as its own end
            // value, so an interrupted squash self-heals. Every other path uses KillAndRestore.
            _target.DOKill();
            _target.DOScale(_baseScale, _releaseDuration)
                .SetEase(Ease.OutBack, _releaseOvershoot)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        // SetLink kills on destroy, not on disable — a panel hidden mid-squash would otherwise come
        // back squashed, and a pointer that lifted while we were inactive never reaches us.
        private void OnDisable()
        {
            _pressingPointers.Clear();
            KillAndRestore();
        }

        private void KillAndRestore() => JuiceScale.KillAndRestore(_target, _baseScale);
    }
}
