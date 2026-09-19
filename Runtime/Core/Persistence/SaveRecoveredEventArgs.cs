namespace Sinkii09.UIFramework
{
    // Raised when a load fell back to the backup copy because the primary was unreadable.
    //
    // Separate from SaveEventArgs on purpose: this is a LOAD-time event, it carries no Exception, and
    // it reports something SaveEventArgs has no room for — whether the damaged primary was repaired
    // afterwards. Repair is best-effort and skips whenever another writer got there first, so a
    // consumer that announced "repaired" on every recovery would be announcing something that may not
    // have happened.
    public readonly struct SaveRecoveredEventArgs
    {
        // The key the game asked for, never the slot-decorated storage key — the decoration is an
        // implementation detail and must not leak into consumer UI.
        public readonly string Key;

        // True when the recovered payload was written back over the primary. False when repair was
        // skipped (another save or a delete raced it) or failed; the recovered data is still valid
        // either way, but the primary is still damaged and the next ordinary save will heal it.
        public readonly bool PrimaryRepaired;

        public SaveRecoveredEventArgs(string key, bool primaryRepaired)
        {
            Key = key;
            PrimaryRepaired = primaryRepaired;
        }
    }
}
