using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    public enum UIButtonSoundMode
    {
        /// <summary>The theme's Toggle cue on a Toggle, its ButtonClick cue on anything else.</summary>
        Default,
        Custom,
        /// <summary>Plays nothing. Marks a button whose sound comes from game feedback instead.</summary>
        Silent,
    }

    /// <summary>Plays a UI sound when the Selectable on this GameObject is used.</summary>
    /// <remarks>
    /// Resolves IAudioService through the parent view on first use, so rows spawned at runtime work.
    /// Each kind listens where only a real press reaches it:
    /// - Button: onClick. EventSystem runs the Button's handler first, so a click handler that disables
    ///   the button would fail an interactable check made afterwards.
    /// - Toggle: the click and the value change must meet in one frame, whichever comes second plays.
    ///   onValueChanged alone would also fire when a binding sets isOn from code.
    /// - Anything else (dropdowns): the click itself, if the control is interactable.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class UIButtonSound : MonoBehaviour, IPointerClickHandler, ISubmitHandler
    {
        [SerializeField] private UIButtonSoundMode _mode;
        [SerializeField] private SoundRef _custom;

        private Selectable _selectable;
        private Button _button;
        private Toggle _toggle;
        private UIViewBase _view;
        private bool _reportedUnresolved;
        private int _pressFrame = -1;
        private int _valueChangedFrame = -1;

        public UIButtonSoundMode Mode => _mode;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
            _button = _selectable as Button;
            _toggle = _selectable as Toggle;
            if (_selectable == null) ReportMissingSelectable();

            if (_button != null) _button.onClick.AddListener(OnButtonClicked);
            if (_toggle != null) _toggle.onValueChanged.AddListener(OnToggleChanged);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
            if (_toggle != null) _toggle.onValueChanged.RemoveListener(OnToggleChanged);
        }

        // A pooled row can move under another view.
        private void OnTransformParentChanged() => _view = null;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) OnPressed();
        }

        public void OnSubmit(BaseEventData eventData) => OnPressed();

        private void OnButtonClicked() => Play();

        private void OnPressed()
        {
            if (_button != null || _selectable == null) return;
            if (_toggle == null)
            {
                if (_selectable.IsActive() && _selectable.IsInteractable()) Play();
                return;
            }
            _pressFrame = Time.frameCount;
            if (_valueChangedFrame == _pressFrame) PlayToggle();
        }

        private void OnToggleChanged(bool _)
        {
            _valueChangedFrame = Time.frameCount;
            if (_pressFrame == _valueChangedFrame) PlayToggle();
        }

        // One press, one sound: a second value change in the same frame (a binding reverting isOn)
        // must not meet the same press again.
        private void PlayToggle()
        {
            _pressFrame = -1;
            _valueChangedFrame = -1;
            Play();
        }

        private void Play()
        {
            if (_mode == UIButtonSoundMode.Silent) return;
            var audio = ResolveAudio();
            if (audio == null) return;

            SoundId id = _mode == UIButtonSoundMode.Custom ? _custom.Id : ThemeCue(audio.Theme);
            if (id.IsValid) audio.Play(id);
        }

        private SoundId ThemeCue(UISoundTheme theme)
        {
            if (theme == null) return SoundId.Invalid;
            return _toggle != null ? theme.Toggle.Id : theme.ButtonClick.Id;
        }

        private IAudioService ResolveAudio()
        {
            if (_view == null) _view = GetComponentInParent<UIViewBase>(true);
            var audio = _view != null ? _view.Audio : null;
            if (audio == null) ReportUnresolved();
            return audio;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void ReportUnresolved()
        {
            if (_reportedUnresolved) return;
            _reportedUnresolved = true;
            Debug.LogError($"[Audio] UIButtonSound on '{name}' has no injected parent view, so it cannot play. " +
                           "Place it under a UIView created by the navigator.", this);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void ReportMissingSelectable() =>
            Debug.LogError($"[Audio] UIButtonSound on '{name}' needs a Button, Toggle or other Selectable on the same GameObject.", this);
    }
}
