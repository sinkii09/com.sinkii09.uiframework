using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // Registered when a game has not built a recovery screen. Same Null-Object shape as
    // NullTransitionOverlay and NullNotificationService: the dependency always resolves, so nothing
    // has to null-check it.
    //
    // Reports Abort, never StartFresh. A null object that silently answered "delete it" would turn
    // every unreadable save into a wiped one with nobody having decided that — the loudest possible
    // failure is the correct default here, because the alternative is invisible data loss.
    public sealed class NullSaveRecoveryPrompt : ISaveRecoveryPrompt
    {
        public UniTask<SaveRecoveryChoice> AskAsync(SaveCorruptionReport report,
                                                    CancellationToken ct = default)
        {
            Debug.LogError(
                $"[SaveRecovery] Save '{report.Key}' could not be read and its backup did not restore " +
                "it. No ISaveRecoveryPrompt is registered, so the player cannot be asked and the " +
                "failure is being propagated with the file left intact. Register a prompt to offer " +
                $"\"continue without it\" instead of a crash. Underlying error: {report.Error}");

            return UniTask.FromResult(SaveRecoveryChoice.Abort);
        }
    }
}
