using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Which save slot the game is currently playing. Injected into the save service, which decorates
    /// every key with it before touching storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Slot 0 writes the undecorated key, so saves written before slots existed are slot 0 and keep
    /// loading unchanged. Slots above 0 append <c>.s{n}</c>, a form no game can produce itself
    /// because <c>.</c> is illegal in a save key.
    /// </para>
    /// <para>
    /// <b>Register this <c>Lifetime.Singleton</c>, never <c>Scoped</c>.</b> It is injected into
    /// <see cref="JsonSaveService"/>, which is a root singleton, while <c>UIViewFactory</c> hands
    /// every view its own child container — a scoped registration would silently fork the context per
    /// view and a slot switch made in one view would never reach the service. This cannot be detected
    /// at runtime: VContainer does not expose a registration's lifetime at injection time, and the
    /// service would simply hold whatever the root scope produced. It is a documented rule, not an
    /// enforced one.
    /// </para>
    /// <para>
    /// Read on the main thread only. The save service reads it exactly once per operation, at key
    /// resolution, and threads that value through — including into the repair path that runs after an
    /// await — so a slot switch can never split one operation across two slots.
    /// </para>
    /// </remarks>
    public interface ISaveSlotContext
    {
        int ActiveSlot { get; }
    }

    /// <summary>
    /// Default <see cref="ISaveSlotContext"/>: starts on slot 0 and can be moved between slots.
    /// </summary>
    public sealed class SaveSlotContext : ISaveSlotContext
    {
        /// <summary>Highest selectable slot index. Slots are 0..<see cref="MaxSlotIndex"/>.</summary>
        public const int MaxSlotIndex = SaveKeyResolver.MaxSlotIndex;

        private int _activeSlot;

        public int ActiveSlot
        {
            get => _activeSlot;
            set
            {
                if (value < 0 || value > MaxSlotIndex)
                {
                    // Logged and ignored rather than thrown: a bad slot index usually arrives from UI
                    // (a slot-select screen), and taking down the frame is a worse answer than staying
                    // on the current slot. Writing to the wrong slot would be worse than both, which
                    // is why the value is refused rather than clamped.
                    Debug.LogError($"[SaveSlotContext] Slot {value} is out of range 0..{MaxSlotIndex}. " +
                                   $"Staying on slot {_activeSlot}.");
                    return;
                }

                _activeSlot = value;
            }
        }
    }
}
