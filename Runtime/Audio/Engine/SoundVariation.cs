using System;

namespace Sinkii09.UIFramework
{
    // Clip choice per container mode. All state lives on the EventDefinition and is sized in Prepare,
    // so picking never allocates.
    internal static class SoundVariation
    {
        public static void Prepare(EventDefinition sound)
        {
            int window = Math.Min(sound.AvoidRepeat, sound.ClipCount - 1);
            sound.Recent = window > 0 ? new int[window] : Array.Empty<int>();
            sound.RecentCount = 0;
            sound.RecentNext = 0;

            sound.Bag = new int[sound.ClipCount];
            for (int i = 0; i < sound.Bag.Length; i++) sound.Bag[i] = i;
            // Exhausted, so the first pick shuffles.
            sound.BagPosition = sound.Bag.Length;
            sound.LastPicked = -1;
        }

        public static int PickClip(EventDefinition sound, ref AudioRandom random)
        {
            if (sound.ClipCount <= 1) return 0;
            switch (sound.Container)
            {
                case SoundContainerMode.Single: return 0;
                case SoundContainerMode.Shuffle: return PickShuffle(sound, ref random);
                default: return PickRandom(sound, ref random);
            }
        }

        private static int PickRandom(EventDefinition sound, ref AudioRandom random)
        {
            int n = sound.ClipCount;
            int k = random.Next(n - sound.RecentCount);
            for (int i = 0; i < n; i++)
            {
                if (IsRecent(sound, i)) continue;
                if (k-- == 0)
                {
                    Remember(sound, i);
                    return i;
                }
            }
            return 0;
        }

        private static bool IsRecent(EventDefinition sound, int clip)
        {
            for (int i = 0; i < sound.RecentCount; i++)
                if (sound.Recent[i] == clip) return true;
            return false;
        }

        private static void Remember(EventDefinition sound, int clip)
        {
            int window = sound.Recent.Length;
            if (window == 0) return;
            sound.Recent[sound.RecentNext] = clip;
            sound.RecentNext = (sound.RecentNext + 1) % window;
            if (sound.RecentCount < window) sound.RecentCount++;
        }

        private static int PickShuffle(EventDefinition sound, ref AudioRandom random)
        {
            int[] bag = sound.Bag;
            if (sound.BagPosition >= bag.Length)
            {
                for (int i = bag.Length - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (bag[i], bag[j]) = (bag[j], bag[i]);
                }
                // The last clip of one round must not open the next.
                if (bag[0] == sound.LastPicked)
                    (bag[0], bag[bag.Length - 1]) = (bag[bag.Length - 1], bag[0]);
                sound.BagPosition = 0;
            }

            int pick = bag[sound.BagPosition++];
            sound.LastPicked = pick;
            return pick;
        }
    }
}
