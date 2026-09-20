using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Load a save, and if it is damaged past repair, ask instead of crashing.
    //
    // Use this at boot in place of ISaveService.LoadAsync. LoadAsync itself is left exactly as it
    // was — same return values, same exception types — because five existing tests pin that
    // behaviour and because a service that silently decides what to do about damaged data is the
    // wrong place for that decision to live.
    public sealed class SaveRecoveryCoordinator
    {
        private readonly ISaveService _saves;
        private readonly ISaveRecoveryPrompt _prompt;

        [Inject]
        public SaveRecoveryCoordinator(ISaveService saves, ISaveRecoveryPrompt prompt)
        {
            _saves = saves ?? throw new ArgumentNullException(nameof(saves));
            _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        }

        /// <summary>
        /// Loads <paramref name="key"/>, offering the player a way out if the payload itself is
        /// unreadable.
        /// </summary>
        /// <returns>
        /// The save; <c>null</c> when there is no save yet, and also <c>null</c> when the player
        /// chose to discard an unreadable one — from the caller's side both mean "start a new game",
        /// which is exactly what StartFresh asked for.
        /// </returns>
        /// <remarks>
        /// Every failure that is not provably a damaged PAYLOAD propagates untouched. That asymmetry
        /// is deliberate and is the whole safety argument: a crash loses a session, and a wrong
        /// "start fresh" loses the save.
        /// </remarks>
        public async UniTask<T> LoadOrAskAsync<T>(string key, CancellationToken ct = default) where T : class
        {
            try
            {
                return await _saves.LoadAsync<T>(key, ct);
            }
            catch (JsonException corruption)
            {
                // An ALLOW-LIST of one, not a list of exclusions. The first version of this excluded
                // the three exception types that must never be answered with "delete it" and treated
                // everything else as damage — which quietly included the ones that are not damage at
                // all. JsonSaveService awaits IStorageBackend.ReadAsync OUTSIDE its try block, so a
                // file locked for a moment by a sync client or a virus scanner surfaces here as a
                // raw IOException. Offering "your save is corrupt, start fresh" for that, and then
                // deleting the primary AND its backup when the player agrees, destroys an intact save
                // over a transient lock.
                //
                // JsonException is what a genuinely damaged payload produces, and it is what the
                // existing load tests assert. Anything else — I/O, permissions, schema version, a
                // broken migration chain, cancellation, a network save service — propagates, because
                // a failure this class does not recognise is not one it may act on.
                return await AskAboutCorruption<T>(key, corruption, ct);
            }
        }

        private async UniTask<T> AskAboutCorruption<T>(string key, JsonException corruption,
                                                       CancellationToken ct) where T : class
        {
            SaveRecoveryChoice choice;
            try
            {
                choice = await _prompt.AskAsync(new SaveCorruptionReport(key, corruption), ct);
            }
            catch (Exception)
            {
                // The prompt is game code and can throw, or be cancelled as the app shuts down.
                // Logging the original first matters: otherwise the only thing anyone ever sees is
                // the recovery screen's own failure, and the actual corruption is never diagnosed.
                Debug.LogError($"[SaveRecovery] Prompt for '{key}' failed; the original corruption was: {corruption}");
                throw;
            }

            if (choice != SaveRecoveryChoice.StartFresh)
            {
                // Capture-and-throw, not `throw corruption`: rethrowing the object directly resets
                // its stack trace, and this is the exception a developer will be reading to find out
                // WHERE the payload went wrong. Same reason JsonSaveService.LoadAsync does it.
                ExceptionDispatchInfo.Capture(corruption).Throw();
            }

            await _saves.DeleteAsync(key, ct);
            return null;
        }
    }
}
