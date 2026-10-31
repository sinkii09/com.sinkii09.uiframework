using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests.Editor
{
    public class SoundRegistryTests
    {
        private readonly List<Object> _created = new();
        private FakeVoiceBackend _backend;
        private AudioEngine _engine;
        private SoundRegistry _registry;
        private SoundGroup _group;

        [SetUp]
        public void SetUp()
        {
            _backend = new FakeVoiceBackend(8);
            _engine = new AudioEngine(_backend, 4, 0.1f, null);
            _registry = new SoundRegistry(_engine);
            _group = Track(ScriptableObject.CreateInstance<SoundGroup>());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        private SoundEvent NewEvent(string path)
        {
            var sound = Track(ScriptableObject.CreateInstance<SoundEvent>());
            sound.Path = path;
            sound.Group = _group;
            sound.Clips = new[] { Track(AudioClip.Create(path, 64, 1, 22050, false)) };
            return sound;
        }

        [Test]
        public void SharedEvent_StaysRegisteredUntilItsLastBankReleases()
        {
            var sound = NewEvent("ui/click");
            _registry.Add(sound);
            _registry.Add(sound);

            Assert.IsFalse(_registry.Release(sound));
            Assert.IsTrue(_engine.TryGetEvent(sound.Id, out _));
            Assert.IsTrue(_registry.Release(sound));
            Assert.IsFalse(_engine.TryGetEvent(sound.Id, out _));
        }

        [Test]
        public void Collision_IsRefused_AndReleasingTheImpostorKeepsTheOwner()
        {
            var owner = NewEvent("ui/click");
            var impostor = NewEvent("ui/click");
            _registry.Add(owner);

            LogAssert.Expect(LogType.Error, new Regex("collision"));
            _registry.Add(impostor);

            Assert.IsFalse(_registry.Release(impostor));
            Assert.IsTrue(_registry.TryGet(owner.Id, out var registered));
            Assert.AreSame(owner, registered);
        }

        [Test]
        public void EventWithoutGroupOrPath_IsSkippedWithError()
        {
            var noGroup = NewEvent("a");
            noGroup.Group = null;
            var noPath = NewEvent("");

            LogAssert.Expect(LogType.Error, new Regex("has no Group"));
            _registry.Add(noGroup);
            LogAssert.Expect(LogType.Error, new Regex("has no Path"));
            _registry.Add(noPath);

            Assert.AreEqual(0, _registry.Count);
        }

        [Test]
        public void LastEventReleased_RemovesItsGroup_SoAReloadedInstanceDoesNotStack()
        {
            var sound = NewEvent("a");
            _registry.Add(sound);
            Assert.AreEqual(1, _engine.GroupCount);

            _registry.Release(sound);
            Assert.AreEqual(0, _engine.GroupCount);
            Assert.IsNull(_registry.GroupFor(_group));

            // Addressables hands back a new instance after the bundle is released and reloaded.
            sound.Group = Track(ScriptableObject.CreateInstance<SoundGroup>());
            _registry.Add(sound);
            Assert.AreEqual(1, _engine.GroupCount);
        }

        [Test]
        public void GroupOverrides_AreAppliedFromTheEvent()
        {
            _group.Priority = 50;
            var sound = NewEvent("a");
            sound.OverridePriority = true;
            sound.Priority = 200;
            _registry.Add(sound);

            _engine.TryGetEvent(sound.Id, out var definition);
            Assert.AreEqual(200, definition.Priority);
            Assert.AreSame(_registry.GroupFor(_group), definition.Group);
        }
    }
}
