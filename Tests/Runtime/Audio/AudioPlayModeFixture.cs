using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sinkii09.UIFramework.Tests
{
    // An IAssetLoader over banks built in memory, so PlayMode audio tests need no assets on disk.
    internal sealed class InMemoryBankLoader : IAssetLoader
    {
        private readonly Dictionary<string, Object> _assets = new();
        public readonly List<string> Unloads = new();

        public void Add(string key, Object asset) => _assets[key] = asset;

        public UniTask<T> LoadAssetAsync<T>(string key, CancellationToken ct = default) where T : Object
            => _assets.TryGetValue(key, out var asset)
                ? UniTask.FromResult((T)asset)
                : UniTask.FromException<T>(new KeyNotFoundException(key));

        public UniTask UnloadAsync(string key, CancellationToken ct = default)
        {
            Unloads.Add(key);
            return UniTask.CompletedTask;
        }
    }

    // Builds the assets a real AudioService needs, and destroys them afterwards.
    internal sealed class AudioPlayModeFixture
    {
        private readonly List<Object> _assets = new();
        private readonly GameObjectTracker _gos = new();

        public AudioPlayModeFixture()
        {
            Signals = _gos.Track(new GameObject("AudioTestRoot").AddComponent<AppLifecycleSignals>());
            Audio = Track(ScriptableObject.CreateInstance<AudioConfig>());
            Audio.PoolSizeDefault = 4;
            Audio.PoolSizeWeb = 4;
            Config = Track(ScriptableObject.CreateInstance<UIFrameworkConfig>());
            Config.Audio = Audio;
            Loader = new InMemoryBankLoader();
        }

        public AppLifecycleSignals Signals { get; }
        public AudioConfig Audio { get; }
        public UIFrameworkConfig Config { get; }
        public InMemoryBankLoader Loader { get; }

        public SoundGroup Group(AudioBus bus = AudioBus.Sfx, int perEvent = 4, int stopFadeMs = 10)
        {
            var group = Track(ScriptableObject.CreateInstance<SoundGroup>());
            group.Bus = bus;
            group.MaxInstancesPerEvent = perEvent;
            group.StopFadeMs = stopFadeMs;
            return group;
        }

        public SoundEvent Event(string path, SoundGroup group, bool loop = false)
        {
            var sound = Track(ScriptableObject.CreateInstance<SoundEvent>());
            sound.Path = path;
            sound.Group = group;
            sound.Loop = loop;
            // 2 s of silence: long enough to still be playing across a test's frames.
            sound.Clips = new[] { Track(AudioClip.Create(path, 44100, 1, 22050, false)) };
            return sound;
        }

        public SoundBank Bank(string key, params SoundEvent[] events)
        {
            var bank = Track(ScriptableObject.CreateInstance<SoundBank>());
            bank.Events = events;
            Loader.Add(key, bank);
            return bank;
        }

        public AudioService NewService() => new(Config, Loader, Signals);

        public int PlayingSources()
        {
            int playing = 0;
            foreach (var source in Signals.GetComponentsInChildren<AudioSource>())
                if (source.isPlaying) playing++;
            return playing;
        }

        public void Destroy()
        {
            _gos.DestroyAll();
            foreach (var asset in _assets) Object.Destroy(asset);
            _assets.Clear();
        }

        private T Track<T>(T asset) where T : Object
        {
            _assets.Add(asset);
            return asset;
        }
    }
}
