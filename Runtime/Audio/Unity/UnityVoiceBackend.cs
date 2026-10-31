using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sinkii09.UIFramework
{
    // A fixed pool of 2D AudioSources on one GameObject under the framework root, created once and
    // never instantiated per play. Fades are per voice and run in Tick on unscaled time, because the
    // game pauses with timeScale = 0 and a scaled fade would never finish in a pause menu.
    internal sealed class UnityVoiceBackend : IVoiceBackend, IDisposable
    {
        private readonly GameObject _root;
        private readonly AudioSource[] _sources;
        private readonly float[] _fadeFrom;
        private readonly float[] _fadeLeft;
        private readonly float[] _fadeTotal;

        public UnityVoiceBackend(Transform parent, int voices)
        {
            _root = new GameObject("UIFramework Audio Voices");
            _root.transform.SetParent(parent, false);
            _sources = new AudioSource[voices];
            _fadeFrom = new float[voices];
            _fadeLeft = new float[voices];
            _fadeTotal = new float[voices];

            for (int i = 0; i < voices; i++)
            {
                var source = _root.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _sources[i] = source;
            }
        }

        public int VoiceCount => _sources.Length;

        // Test seam.
        internal AudioSource SourceAt(int voice) => _sources[voice];

        public void Start(int voice, EventDefinition sound, int clipIndex, float gain, float pitch)
        {
            var source = _sources[voice];
            var clips = ((SoundEvent)sound.Payload).Clips;
            // A null clip plays nothing; the engine reclaims the voice next frame because it never plays.
            source.clip = clipIndex < clips.Length ? clips[clipIndex] : null;
            source.loop = sound.Loop;
            source.volume = gain;
            source.pitch = pitch;
            _fadeLeft[voice] = 0f;
            source.Play();
        }

        public void Stop(int voice, int fadeMs)
        {
            if (fadeMs <= 0)
            {
                Cut(voice);
                return;
            }
            _fadeFrom[voice] = _sources[voice].volume;
            _fadeTotal[voice] = _fadeLeft[voice] = fadeMs / 1000f;
        }

        public void SetGain(int voice, float gain) => _sources[voice].volume = gain;

        public bool IsFinished(int voice) => _fadeLeft[voice] <= 0f && !_sources[voice].isPlaying;

        public void Tick(float deltaSeconds)
        {
            for (int v = 0; v < _sources.Length; v++)
            {
                if (_fadeLeft[v] <= 0f) continue;
                _fadeLeft[v] -= deltaSeconds;
                if (_fadeLeft[v] <= 0f) Cut(v);
                else _sources[v].volume = _fadeFrom[v] * (_fadeLeft[v] / _fadeTotal[v]);
            }
        }

        // Dropping the clip matters: a source still referencing a clip from an unloaded bank keeps
        // that clip's memory alive.
        private void Cut(int voice)
        {
            var source = _sources[voice];
            source.Stop();
            source.clip = null;
            _fadeLeft[voice] = 0f;
        }

        public void Dispose()
        {
            if (_root == null) return;
            if (Application.isPlaying) Object.Destroy(_root);
            else Object.DestroyImmediate(_root);
        }
    }
}
