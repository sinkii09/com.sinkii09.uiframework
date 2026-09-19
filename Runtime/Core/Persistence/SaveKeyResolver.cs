using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Sinkii09.UIFramework
{
    // Turns a game-supplied save key plus an active slot into the key used on storage.
    //
    // Split out of JsonSaveService so the strict validator has exactly one home, and so the ordering
    // rule below is enforced by the shape of the API rather than by everyone remembering it.
    //
    // THE ORDERING RULE, which is a security boundary and not a style preference:
    //
    //     ValidateKey(userKey)   ->   StorageKeyFor(userKey, slot)   ->   never validated again
    //
    // ValidateKey is the ONLY validator in the persistence system, and LocalFileStorageBackend
    // concatenates straight into Path.Combine(root, key + ".json"). If a looser pattern were ever
    // introduced to "allow" the decorated form, '.' would become legal in a user key and '..' would
    // reach Path.Combine — path traversal. So the strict pattern never changes, and decoration
    // happens strictly after it, producing a form that is unreachable by anything a game can name.
    internal static class SaveKeyResolver
    {
        // ColorStackSort/Tests/Editor/FakeSaveService.cs keeps a COPY of this pattern. It stays in
        // sync only because this is never widened — widening it here would desync that fake silently.
        private static readonly Regex KeyPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

        // Bounded because the per-key gate dictionary in JsonSaveService is deliberately never
        // cleared: the lock space is {keys} x {slots}, which is a non-issue while slots are few and
        // a slow leak if a game can mint them freely.
        internal const int MaxSlotIndex = 15;

        internal static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key) || !KeyPattern.IsMatch(key))
                throw new ArgumentException($"Save key '{key}' must match ^[A-Za-z0-9_-]+$.", nameof(key));
        }

        // Slot 0 returns the key untouched, so every save written before slots existed IS slot 0 and
        // keeps loading with no migration of any kind.
        //
        // The slot is treated as untrusted input even though the default implementation bounds it:
        // ISaveSlotContext is a public interface a game implements, so the value arriving here has
        // passed through code this package does not control. InvariantCulture because a negative
        // value under some cultures formats with a non-ASCII sign, which would then be spliced into
        // a file name.
        internal static string StorageKeyFor(string key, int slot)
        {
            if (slot < 0 || slot > MaxSlotIndex)
                throw new ArgumentOutOfRangeException(nameof(slot), slot,
                    $"Active save slot must be between 0 and {MaxSlotIndex}.");

            return slot == 0
                ? key
                : key + ".s" + slot.ToString(CultureInfo.InvariantCulture);
        }
    }
}
