using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Sinkii09.UIFramework
{
    /// <summary>What the player chose to do about a save that will not load.</summary>
    public enum SaveRecoveryChoice
    {
        /// <summary>
        /// Delete the unreadable payload and continue as a new player. Destroys that save for good —
        /// only ever the result of someone actually choosing it.
        /// </summary>
        StartFresh,

        /// <summary>
        /// Leave the file alone and let the failure propagate. The caller decides what happens next:
        /// quit, retry later, run without saving. This is also what an absent prompt reports, so the
        /// default for a game that has not built a recovery screen is "do not touch the data".
        /// </summary>
        Abort,
    }

    /// <summary>Everything the prompt needs to describe the failure to a player.</summary>
    public readonly struct SaveCorruptionReport
    {
        /// <summary>The logical save key, as the game knows it — never the slot-decorated one.</summary>
        public readonly string Key;

        /// <summary>
        /// Why the load failed, after backup recovery had already been tried and failed too.
        /// Diagnostics for a log, not text to put in front of a player.
        /// </summary>
        public readonly Exception Error;

        public SaveCorruptionReport(string key, Exception error)
        {
            Key = key;
            Error = error;
        }
    }

    // Asks the player what to do when a save is damaged beyond what the backup can repair.
    //
    // The framework owns the DECISION POINT, not the screen. It ships no prefabs, so the view is the
    // game's — see TransitionOverlayView and NotificationHostView for the same resident-view shape.
    // Registering an implementation is opt-in; without one, NullSaveRecoveryPrompt reports Abort and
    // nothing is deleted.
    //
    // Reached only through SaveRecoveryCoordinator. It is deliberately NOT wired into
    // ISaveService.LoadAsync: that method's contract says null means "no save yet", so a prompt that
    // returned null there would make every caller start a new game and overwrite the damaged file —
    // turning a recoverable situation into permanent loss, which is the one thing worse than the
    // crash this feature exists to replace.
    public interface ISaveRecoveryPrompt
    {
        UniTask<SaveRecoveryChoice> AskAsync(SaveCorruptionReport report, CancellationToken ct = default);
    }
}
