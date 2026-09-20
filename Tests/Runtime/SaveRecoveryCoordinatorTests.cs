using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests
{
    // The decision the coordinator exists to make: which load failures may a player be offered a
    // "discard it" button for, and which must never be.
    //
    // Two of the five branches are asserted NEGATIVELY — that the prompt is not even shown — because
    // those are the ones where offering the button is the bug. A save from a newer build and a
    // broken migration chain both mean the DATA is probably fine and the BUILD is wrong; deleting in
    // either case destroys real progress over a fixable problem.
    public class SaveRecoveryCoordinatorTests
    {
        private const string Key = "RecoveryTestKey";

        [UnityTest]
        public IEnumerator ReturnsTheSave_WhenNothingIsWrong() => UniTask.ToCoroutine(async () =>
        {
            var expected = new RecoveryPayload { Value = 5 };
            var saves = new StubSaveService { Loaded = expected };
            var prompt = new RecordingPrompt(SaveRecoveryChoice.StartFresh);

            var result = await new SaveRecoveryCoordinator(saves, prompt).LoadOrAskAsync<RecoveryPayload>(Key);

            Assert.That(result, Is.SameAs(expected));
            Assert.That(prompt.Asked, Is.Zero, "A healthy load must not involve the player at all.");
        });

        [UnityTest]
        public IEnumerator DiscardsTheFile_WhenThePlayerChoosesToStartFresh() => UniTask.ToCoroutine(async () =>
        {
            var saves = new StubSaveService { LoadError = new JsonReaderException("mangled json") };
            var prompt = new RecordingPrompt(SaveRecoveryChoice.StartFresh);

            var result = await new SaveRecoveryCoordinator(saves, prompt).LoadOrAskAsync<RecoveryPayload>(Key);

            Assert.That(result, Is.Null, "Null here means the same thing as 'no save yet' — start a new game.");
            Assert.That(saves.Deleted, Is.EqualTo(new[] { Key }));
            Assert.That(prompt.LastReport.Key, Is.EqualTo(Key));
            Assert.That(prompt.LastReport.Error, Is.SameAs(saves.LoadError),
                "The prompt must receive the real cause, not a re-wrapped one.");
        });

        [UnityTest]
        public IEnumerator RethrowsAndKeepsTheFile_WhenThePlayerAborts() => UniTask.ToCoroutine(async () =>
        {
            var original = new JsonReaderException("mangled json");
            var saves = new StubSaveService { LoadError = original };
            var prompt = new RecordingPrompt(SaveRecoveryChoice.Abort);

            var caught = await Capture(new SaveRecoveryCoordinator(saves, prompt));

            Assert.That(caught, Is.SameAs(original), "The original failure must survive the round trip.");
            Assert.That(saves.Deleted, Is.Empty, "Abort must leave the damaged file exactly where it is.");
        });

        [UnityTest]
        public IEnumerator NeverAsksAboutASaveFromANewerBuild() => UniTask.ToCoroutine(async () =>
        {
            // Downgrading once must not cost the player everything.
            var saves = new StubSaveService { LoadError = new SaveSchemaVersionException(Key, 4, 2) };
            var prompt = new RecordingPrompt(SaveRecoveryChoice.StartFresh);

            var caught = await Capture(new SaveRecoveryCoordinator(saves, prompt));

            Assert.That(caught, Is.InstanceOf<SaveSchemaVersionException>());
            Assert.That(prompt.Asked, Is.Zero);
            Assert.That(saves.Deleted, Is.Empty);
        });

        [UnityTest]
        public IEnumerator NeverAsksAboutABrokenMigrationChain() => UniTask.ToCoroutine(async () =>
        {
            // A code defect. The fix is a patch, not a wipe.
            var saves = new StubSaveService { LoadError = new SaveMigrationException(Key, 1, 2, "step returned null") };
            var prompt = new RecordingPrompt(SaveRecoveryChoice.StartFresh);

            var caught = await Capture(new SaveRecoveryCoordinator(saves, prompt));

            Assert.That(caught, Is.InstanceOf<SaveMigrationException>());
            Assert.That(prompt.Asked, Is.Zero);
            Assert.That(saves.Deleted, Is.Empty);
        });

        [UnityTest]
        public IEnumerator NeverAsksAboutATransientIoFailure() => UniTask.ToCoroutine(async () =>
        {
            // The defect this test exists for: JsonSaveService awaits IStorageBackend.ReadAsync
            // OUTSIDE its try block, so a file locked for a moment by a sync client or a virus
            // scanner arrives here as a raw IOException. The first version of the coordinator
            // recognised corruption by EXCLUSION and swept this in — offering "your save is
            // corrupt" for an intact file, and deleting it and its backup when the player agreed.
            var saves = new StubSaveService { LoadError = new IOException("file is locked") };
            var prompt = new RecordingPrompt(SaveRecoveryChoice.StartFresh);

            var caught = await Capture(new SaveRecoveryCoordinator(saves, prompt));

            Assert.That(caught, Is.InstanceOf<IOException>());
            Assert.That(prompt.Asked, Is.Zero, "A locked file is not damaged data.");
            Assert.That(saves.Deleted, Is.Empty, "An intact save must survive a transient failure.");
        });

        [UnityTest]
        public IEnumerator PromptFailure_ReportsTheOriginalCorruptionToo() => UniTask.ToCoroutine(async () =>
        {
            // A recovery screen that throws would otherwise bury the only diagnosis of WHY the save
            // was unreadable, leaving a bug report about the recovery screen and nothing else.
            var saves = new StubSaveService { LoadError = new JsonReaderException("mangled json") };
            var prompt = new ThrowingPrompt();

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("mangled json"));
            var caught = await Capture(new SaveRecoveryCoordinator(saves, prompt));

            Assert.That(caught, Is.InstanceOf<NotSupportedException>(), "The prompt's own failure surfaces.");
            Assert.That(saves.Deleted, Is.Empty);
        });

        [UnityTest]
        public IEnumerator DoesNotTreatCancellationAsCorruption() => UniTask.ToCoroutine(async () =>
        {
            // Quitting during a load would otherwise pop a "your save is damaged" prompt on the way out.
            var saves = new StubSaveService { LoadError = new OperationCanceledException() };
            var prompt = new RecordingPrompt(SaveRecoveryChoice.StartFresh);

            var caught = await Capture(new SaveRecoveryCoordinator(saves, prompt));

            Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
            Assert.That(prompt.Asked, Is.Zero);
        });

        [UnityTest]
        public IEnumerator NullPrompt_AbortsAndSaysWhy() => UniTask.ToCoroutine(async () =>
        {
            // The default for a game that has not built a recovery screen. Answering StartFresh here
            // would wipe saves with nobody having chosen it.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SaveRecovery.*ISaveRecoveryPrompt"));

            var choice = await new NullSaveRecoveryPrompt()
                .AskAsync(new SaveCorruptionReport(Key, new JsonReaderException("boom")));

            Assert.That(choice, Is.EqualTo(SaveRecoveryChoice.Abort));
        });

        private static async UniTask<Exception> Capture(SaveRecoveryCoordinator coordinator)
        {
            try
            {
                await coordinator.LoadOrAskAsync<RecoveryPayload>(Key);
            }
            catch (Exception ex)
            {
                return ex;
            }

            Assert.Fail("Expected the load to fail.");
            return null;
        }

        // --- fakes ---------------------------------------------------------------------------

        internal sealed class RecoveryPayload
        {
            public int Value;
        }

        private sealed class RecordingPrompt : ISaveRecoveryPrompt
        {
            private readonly SaveRecoveryChoice _answer;

            internal RecordingPrompt(SaveRecoveryChoice answer) => _answer = answer;

            internal int Asked { get; private set; }
            internal SaveCorruptionReport LastReport { get; private set; }

            public UniTask<SaveRecoveryChoice> AskAsync(SaveCorruptionReport report, CancellationToken ct = default)
            {
                Asked++;
                LastReport = report;
                return UniTask.FromResult(_answer);
            }
        }

        private sealed class ThrowingPrompt : ISaveRecoveryPrompt
        {
            public UniTask<SaveRecoveryChoice> AskAsync(SaveCorruptionReport report, CancellationToken ct = default)
                => throw new NotSupportedException("no recovery screen wired up");
        }

        private sealed class StubSaveService : ISaveService
        {
            internal object Loaded { get; set; }
            internal Exception LoadError { get; set; }
            internal List<string> Deleted { get; } = new();

            public UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : class
                => LoadError != null ? throw LoadError : UniTask.FromResult((T)Loaded);

            public UniTask<bool> DeleteAsync(string key, CancellationToken ct = default)
            {
                Deleted.Add(key);
                return UniTask.FromResult(true);
            }

            public UniTask<T> LoadAsync<T>(CancellationToken ct = default) where T : class
                => LoadAsync<T>(typeof(T).Name, ct);

            public UniTask<bool> DeleteAsync<T>(CancellationToken ct = default) where T : class
                => DeleteAsync(typeof(T).Name, ct);

            // Unreachable from the coordinator; throwing keeps a future change from passing quietly.
            public UniTask SaveAsync<T>(T data, CancellationToken ct = default) where T : class
                => throw new NotSupportedException();

            public UniTask SaveAsync<T>(string key, T data, CancellationToken ct = default) where T : class
                => throw new NotSupportedException();

            public UniTask<bool> ExistsAsync<T>(CancellationToken ct = default) where T : class
                => throw new NotSupportedException();

            public UniTask<bool> ExistsAsync(string key, CancellationToken ct = default)
                => throw new NotSupportedException();

            public Observable<SaveEventArgs> OnSaveStartedAsObservable => Observable.Empty<SaveEventArgs>();
            public Observable<SaveEventArgs> OnSaveCompletedAsObservable => Observable.Empty<SaveEventArgs>();
            public Observable<SaveEventArgs> OnSaveFailedAsObservable => Observable.Empty<SaveEventArgs>();
        }
    }
}
