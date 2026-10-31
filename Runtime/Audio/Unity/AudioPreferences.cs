using System;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>The player's audio switches. Device-wide, kept in PlayerPrefs.</summary>
    /// <remarks>
    /// Not in ISaveService: its keys are per save slot, and switching slots must not flip the
    /// player's sound settings. A read that finds anything unexpected gives the default (on); a
    /// settings value is never worth a save-recovery prompt. The Sfx switch also drives the Ui bus,
    /// because players expect "sound effects off" to silence button clicks too.
    /// A sealed class rather than an interface, so volume sliders can be added without a breaking change.
    /// </remarks>
    public sealed class AudioPreferences
    {
        internal const string DefaultKeyPrefix = "uifw.audio.";

        private readonly string _musicKey;
        private readonly string _sfxKey;
        private bool _musicEnabled;
        private bool _sfxEnabled;

        internal AudioPreferences(string keyPrefix = DefaultKeyPrefix)
        {
            _musicKey = keyPrefix + "music";
            _sfxKey = keyPrefix + "sfx";
            _musicEnabled = Read(_musicKey);
            _sfxEnabled = Read(_sfxKey);
        }

        internal event Action Changed;

        public bool MusicEnabled
        {
            get => _musicEnabled;
            set
            {
                if (_musicEnabled == value) return;
                _musicEnabled = value;
                Write(_musicKey, value);
                Changed?.Invoke();
            }
        }

        public bool SfxEnabled
        {
            get => _sfxEnabled;
            set
            {
                if (_sfxEnabled == value) return;
                _sfxEnabled = value;
                Write(_sfxKey, value);
                Changed?.Invoke();
            }
        }

        // 0 is off; anything else, including a value of the wrong type (GetInt then returns the
        // default), reads as on.
        private static bool Read(string key) => PlayerPrefs.GetInt(key, 1) != 0;

        // Saved at once: these change on a deliberate tap, so there is nothing to debounce. Sliders,
        // which change every frame while dragged, will need a debounce when they arrive.
        private static void Write(string key, bool value)
        {
            try
            {
                PlayerPrefs.SetInt(key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch (PlayerPrefsException ex)
            {
                // WebGL caps PlayerPrefs storage; the setting still applies for this session.
                Debug.LogWarning($"[Audio] Could not save '{key}': {ex.Message}");
            }
        }
    }
}
