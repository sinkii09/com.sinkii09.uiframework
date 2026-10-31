using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sinkii09.UIFramework
{
    // Two dedicated sources ("decks") outside the voice pool, so a crossfade never competes with SFX
    // for voices. An equal-power curve (sin of the fade weight) keeps loudness steady across the fade.
    // Gain is event volume times the engine's bus gain, so mute, ducking and Master all apply.
    internal sealed class MusicPlayer : IDisposable
    {
        private sealed class Deck
        {
            public AudioSource Source;
            public SoundEvent Sound;
            public AudioBus Bus;
            public float VoiceLinear;
            public float Weight;
            public float Target;
            public float Rate;
        }

        private readonly GameObject _root;
        private readonly Deck[] _decks = new Deck[2];
        private int _active;

        public MusicPlayer(Transform parent)
        {
            _root = new GameObject("UIFramework Music");
            _root.transform.SetParent(parent, false);
            for (int i = 0; i < _decks.Length; i++)
            {
                var source = _root.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _decks[i] = new Deck { Source = source };
            }
        }

        // The track playing or fading in; null when silent or only fading out.
        public SoundEvent Current => _decks[_active].Target > 0f ? _decks[_active].Sound : null;

        // Test seam.
        internal AudioSource SourceAt(int deck) => _decks[deck].Source;

        public void Play(SoundEvent sound, AudioClip clip, AudioBus bus, float voiceLinear, int fadeMs)
        {
            if (Current == sound) return;

            FadeOut(_decks[_active], fadeMs);
            _active ^= 1;
            var deck = _decks[_active];

            deck.Source.Stop();
            deck.Source.clip = clip;
            deck.Source.loop = sound.Loop;
            deck.Sound = sound;
            deck.Bus = bus;
            deck.VoiceLinear = voiceLinear;
            deck.Target = 1f;
            deck.Rate = RateFor(fadeMs);
            deck.Weight = fadeMs <= 0 ? 1f : 0f;
            deck.Source.volume = 0f;
            deck.Source.Play();
        }

        public void Stop(int fadeMs)
        {
            for (int i = 0; i < _decks.Length; i++) FadeOut(_decks[i], fadeMs);
        }

        // Called before a bank's clips are released.
        public void StopIfPlaying(SoundEvent sound)
        {
            for (int i = 0; i < _decks.Length; i++)
                if (_decks[i].Sound == sound) Cut(_decks[i]);
        }

        public void Tick(float deltaSeconds, AudioEngine engine)
        {
            for (int i = 0; i < _decks.Length; i++)
            {
                var deck = _decks[i];
                if (deck.Sound == null) continue;

                if (deck.Weight != deck.Target)
                {
                    float step = deck.Rate * deltaSeconds;
                    deck.Weight = deck.Target > deck.Weight
                        ? Math.Min(deck.Target, deck.Weight + step)
                        : Math.Max(deck.Target, deck.Weight - step);
                }

                if (deck.Target <= 0f && deck.Weight <= 0f)
                {
                    Cut(deck);
                    continue;
                }

                float gain = Math.Min(1f, deck.VoiceLinear * engine.GetBusLinear(deck.Bus));
                deck.Source.volume = gain * Mathf.Sin(deck.Weight * Mathf.PI * 0.5f);
            }
        }

        private static void FadeOut(Deck deck, int fadeMs)
        {
            if (deck.Sound == null) return;
            if (fadeMs <= 0)
            {
                Cut(deck);
                return;
            }
            deck.Target = 0f;
            deck.Rate = RateFor(fadeMs);
        }

        private static void Cut(Deck deck)
        {
            deck.Source.Stop();
            deck.Source.clip = null;
            deck.Sound = null;
            deck.Weight = 0f;
            deck.Target = 0f;
        }

        private static float RateFor(int fadeMs) => fadeMs <= 0 ? float.PositiveInfinity : 1000f / fadeMs;

        public void Dispose()
        {
            if (_root == null) return;
            if (Application.isPlaying) Object.Destroy(_root);
            else Object.DestroyImmediate(_root);
        }
    }
}
