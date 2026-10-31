namespace Sinkii09.UIFramework
{
    // Stopping, freeing and the per-frame tick.
    internal sealed partial class AudioEngine
    {
        public bool IsAlive(in PlayHandle handle) => Resolve(handle) >= 0;

        public bool IsPlaying(in PlayHandle handle)
        {
            int slot = Resolve(handle);
            return slot >= 0 && _slots[slot].State == InstanceState.Playing;
        }

        public void Stop(in PlayHandle handle, int fadeMs)
        {
            int slot = Resolve(handle);
            if (slot >= 0) StopSlot(slot, fadeMs);
        }

        public void StopGroup(GroupDefinition group, int fadeMs)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].State != InstanceState.Free && ReferenceEquals(_slots[i].Event.Group, group))
                    StopSlot(i, fadeMs);
        }

        // Cuts everything at once; used on teardown.
        public void StopAll()
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].State != InstanceState.Free) Kill(i);
        }

        public void Tick(float deltaSeconds)
        {
            float dt = deltaSeconds < 0f ? 0f : deltaSeconds > _maxTickDelta ? _maxTickDelta : deltaSeconds;
            _now += dt;

            if (!Paused) StartDuePending();
            AdvanceDucking(dt);
            // A paused source reports isPlaying == false; reclaiming then would free every voice that
            // is merely paused and hand it to the next request.
            if (!Paused) Reclaim();
            PushGains();
        }

        private int Resolve(in PlayHandle handle)
        {
            int slot = handle.Slot;
            if ((uint)slot >= (uint)_slots.Length) return -1;
            ref var instance = ref _slots[slot];
            return instance.State != InstanceState.Free && instance.Generation == handle.Generation ? slot : -1;
        }

        private void StopSlot(int slot, int fadeMs)
        {
            ref var instance = ref _slots[slot];
            switch (instance.State)
            {
                case InstanceState.Pending:
                    FreeSlot(slot);
                    break;
                case InstanceState.Playing:
                    LeavePlaying(ref instance);
                    instance.State = InstanceState.Stopping;
                    _backend.Stop(instance.Voice, fadeMs < 0 ? instance.Event.Group.StopFadeMs : fadeMs);
                    break;
                case InstanceState.Stopping:
                    // Already fading; only an explicit cut overrides the fade in progress.
                    if (fadeMs == 0) _backend.Stop(instance.Voice, 0);
                    break;
            }
        }

        private void Kill(int slot)
        {
            ref var instance = ref _slots[slot];
            if (instance.State == InstanceState.Playing) LeavePlaying(ref instance);
            if (instance.Voice >= 0)
            {
                _backend.Stop(instance.Voice, 0);
                ReleaseVoice(ref instance);
            }
            FreeSlot(slot);
        }

        private void StartDuePending()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref var instance = ref _slots[i];
                if (instance.State != InstanceState.Pending || instance.StartAt > _now) continue;

                // Gates again: the context may have locked, or the bus been muted, while it waited.
                var bus = instance.Event.Group.Bus;
                if (IsMuted(bus) || (bus != AudioBus.Music && !ContextRunning))
                {
                    FreeSlot(i);
                    _counters.Dropped++;
                }
                else if (!TryStart(i))
                {
                    FreeSlot(i);
                    _counters.Rejected++;
                }
            }
        }

        private void Reclaim()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref var instance = ref _slots[i];
                if (instance.Voice < 0 || !_backend.IsFinished(instance.Voice)) continue;

                if (instance.State == InstanceState.Playing) LeavePlaying(ref instance);
                // Releases the clip reference so an unloaded bank's clips are not kept alive.
                _backend.Stop(instance.Voice, 0);
                ReleaseVoice(ref instance);
                FreeSlot(i);
            }
        }

        private void PushGains()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref var instance = ref _slots[i];
                if (instance.State != InstanceState.Playing) continue;
                float gain = VoiceGain(instance.VoiceLinear, instance.Event.Group.Bus);
                if (gain == instance.AppliedGain) continue;
                instance.AppliedGain = gain;
                _backend.SetGain(instance.Voice, gain);
            }
        }

        private void LeavePlaying(ref InstanceSlot instance)
        {
            instance.Event.Playing--;
            instance.Event.Group.Playing--;
            _playingTotal--;
        }

        private void ReleaseVoice(ref InstanceSlot instance)
        {
            _freeVoices[_freeVoiceCount++] = instance.Voice;
            instance.Voice = -1;
        }

        private void FreeSlot(int slot)
        {
            ref var instance = ref _slots[slot];
            instance.State = InstanceState.Free;
            instance.Event = null;
            instance.Generation++;
        }
    }
}
