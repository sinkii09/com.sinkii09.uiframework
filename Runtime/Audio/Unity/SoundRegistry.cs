using System.Collections.Generic;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Which SoundEvent assets are registered, and how many loaded banks hold each. An event shared by
    // two banks stays registered (and keeps playing) until the last of them unloads. A DIFFERENT asset
    // with the same id is a hard error: it is refused, never allowed to alias the first.
    internal sealed class SoundRegistry
    {
        private sealed class Entry
        {
            public SoundEvent Asset;
            public int References;
        }

        private readonly AudioEngine _engine;
        private readonly Dictionary<SoundId, Entry> _entries = new();
        private sealed class GroupEntry
        {
            public GroupDefinition Definition;
            public int Events;
        }

        private readonly Dictionary<SoundGroup, GroupEntry> _groups = new();

        public SoundRegistry(AudioEngine engine) => _engine = engine;

        public int Count => _entries.Count;

        public bool TryGet(SoundId id, out SoundEvent asset)
        {
            asset = _entries.TryGetValue(id, out var entry) ? entry.Asset : null;
            return asset != null;
        }

        public GroupDefinition GroupFor(SoundGroup group)
            => group != null && _groups.TryGetValue(group, out var entry) ? entry.Definition : null;

        public void Add(SoundEvent asset)
        {
            if (asset == null)
            {
                Debug.LogError("[Audio] A bank lists a missing SoundEvent; skipped.");
                return;
            }

            var id = asset.Id;
            if (!id.IsValid)
            {
                Debug.LogError($"[Audio] SoundEvent '{asset.name}' has no Path, so it has no id; skipped.", asset);
                return;
            }

            if (_entries.TryGetValue(id, out var existing))
            {
                if (existing.Asset == asset)
                {
                    existing.References++;
                    return;
                }
                Debug.LogError($"[Audio] SoundId collision: '{asset.name}' ({asset.Path}) and '{existing.Asset.name}' " +
                               $"({existing.Asset.Path}) share {id}. The second was refused; rename one path.", asset);
                return;
            }

            if (asset.Group == null)
            {
                Debug.LogError($"[Audio] SoundEvent '{asset.Path}' has no Group, so it has no bus or rules; skipped.", asset);
                return;
            }

            WarnOnNullClips(asset);
            var group = GetOrCreateGroup(asset.Group);
            var definition = SoundDefinitionFactory.BuildEvent(asset, group.Definition);
            if (!_engine.RegisterEvent(definition))
            {
                ReleaseGroup(asset.Group, group);
                return;
            }
            group.Events++;
            _entries.Add(id, new Entry { Asset = asset, References = 1 });
        }

        // True when this was the last reference, meaning the event is now gone from the engine.
        public bool Release(SoundEvent asset)
        {
            if (asset == null) return false;
            var id = asset.Id;
            // A refused (colliding) asset never took a reference, so it must not drop the owner's.
            if (!_entries.TryGetValue(id, out var entry) || entry.Asset != asset) return false;
            if (--entry.References > 0) return false;

            _entries.Remove(id);
            _engine.UnregisterEvent(id);
            if (asset.Group != null && _groups.TryGetValue(asset.Group, out var group))
            {
                group.Events--;
                ReleaseGroup(asset.Group, group);
            }
            return true;
        }

        // Keyed by asset instance: Addressables hands out a NEW instance after a bundle is released
        // and reloaded, so a group must leave the engine with its last event, or every reload would
        // stack another reservation and keep the bus's ducker slot.
        private GroupEntry GetOrCreateGroup(SoundGroup group)
        {
            if (_groups.TryGetValue(group, out var entry)) return entry;
            entry = new GroupEntry { Definition = SoundDefinitionFactory.BuildGroup(group) };
            _groups.Add(group, entry);
            _engine.RegisterGroup(entry.Definition);
            return entry;
        }

        private void ReleaseGroup(SoundGroup group, GroupEntry entry)
        {
            if (entry.Events > 0) return;
            _groups.Remove(group);
            _engine.UnregisterGroup(entry.Definition);
        }

        private static void WarnOnNullClips(SoundEvent asset)
        {
            var clips = asset.Clips;
            if (clips == null) return;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null) continue;
                Debug.LogWarning($"[Audio] SoundEvent '{asset.Path}' has an empty clip slot {i}; that pick plays nothing.", asset);
                return;
            }
        }
    }
}
