using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System.Text.RegularExpressions;
using NUnit.Framework;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Repair-on-recover. Recovering from the backup used to leave the primary corrupt, so the next
    // ordinary save rotated that corrupt file into the backup slot and destroyed the last good copy —
    // recovery bought one session and then threw the lifeline away.
    public class SaveRepairOnRecoverTests
    {
        private const string Key = "RepairTestKey";
        private const string Corrupt = "{ this is not json";

        [TearDown]
        public void ResetLogAssert()
        {
            // Several tests here silence expected error logs. Left set, that would mask a genuine
            // error in whatever test ran next.
            LogAssert.ignoreFailingMessages = false;
        }

        private static JsonSaveService NewService(FakeStorageBackend backend)
            => new(backend, SaveMigrationRegistry.Empty, new SaveSlotContext());

        private static FakeStorageBackend CorruptPrimaryWithGoodBackup()
        {
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, Corrupt);
            backend.SeedBackup(Key, CurrentEnvelope(Sample()));
            return backend;
        }

        [UnityTest]
        public IEnumerator Recovery_RepairsThePrimary() => UniTask.ToCoroutine(async () =>
        {
            var backend = CorruptPrimaryWithGoodBackup();
            LogAssert.ignoreFailingMessages = true;

            var loaded = await NewService(backend).LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(42, loaded.Score, "The recovered value is still returned.");
            Assert.AreEqual(CurrentEnvelope(Sample()), backend.PeekPrimary(Key),
                "The primary must hold the recovered payload, byte for byte.");
        });

        [UnityTest]
        public IEnumerator AfterRepair_TheNextSaveRotatesAGoodCopyIntoBackup() => UniTask.ToCoroutine(async () =>
        {
            // The actual point of the whole exercise. Without repair, this next save would rotate the
            // CORRUPT primary into the backup slot and the last good copy would be gone.
            var backend = CorruptPrimaryWithGoodBackup();
            LogAssert.ignoreFailingMessages = true;
            var service = NewService(backend);

            await service.LoadAsync<TestSaveData>(Key);
            await service.SaveAsync(Key, new TestSaveData { Name = "next", Score = 7 });

            var backup = await backend.ReadBackupAsync(Key);
            Assert.AreNotEqual(Corrupt, backup, "The corrupt payload must not have been rotated into the backup.");
            Assert.AreEqual(CurrentEnvelope(Sample()), backup,
                "The backup now holds the repaired payload, so a second failure would still be recoverable.");

            // And the whole chain still works: the new save is what loads back.
            Assert.AreEqual(7, (await service.LoadAsync<TestSaveData>(Key)).Score);
        });

        [UnityTest]
        public IEnumerator RepairFailure_StillReturnsTheRecoveredValue() => UniTask.ToCoroutine(async () =>
        {
            // If repair threw, the exception would reach a consumer that reads it as "corrupt, start
            // fresh", re-enables saving, and overwrites real progress on the next write — a worse bug
            // than the one being fixed.
            var backend = CorruptPrimaryWithGoodBackup();
            backend.WriteError = new InvalidOperationException("disk full (simulated)");
            LogAssert.ignoreFailingMessages = true;

            var loaded = await NewService(backend).LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(42, loaded.Score);
            Assert.AreEqual(Corrupt, backend.PeekPrimary(Key), "Repair failed, so the primary is untouched.");
        });

        // --- Races. The hook fires during ReadBackupAsync, i.e. between the failed primary parse and
        // --- the repair path re-reading the primary — exactly the window that matters.

        [UnityTest]
        public IEnumerator RacingSave_MakesRepairSkip_NoLostUpdate() => UniTask.ToCoroutine(async () =>
        {
            // Without the re-read guard, repair would write the OLDER backup payload over a save that
            // completed while the load was in flight — a lost update that does not exist today.
            var backend = CorruptPrimaryWithGoodBackup();
            var newer = CurrentEnvelope(new TestSaveData { Name = "newer", Score = 99 });
            backend.OnReadBackup = () => backend.SeedPrimary(Key, newer);
            LogAssert.ignoreFailingMessages = true;

            await NewService(backend).LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(newer, backend.PeekPrimary(Key), "The racing save must survive untouched.");
        });

        [UnityTest]
        public IEnumerator RacingDelete_MakesRepairSkip_NoResurrection() => UniTask.ToCoroutine(async () =>
        {
            // A guard that read "the file is gone, so put it back" would resurrect a save the player
            // deliberately deleted. Absence means skip, never write.
            var backend = CorruptPrimaryWithGoodBackup();
            backend.OnReadBackup = () => backend.DeleteAsync(Key).Forget();
            LogAssert.ignoreFailingMessages = true;

            // Asserting only "the primary is still gone" would NOT prove the null guard: with that
            // branch deleted, the ordinal comparison against the corrupt payload also fails and repair
            // skips anyway, so the test would pass against broken code. Pinning the specific log is
            // what distinguishes "skipped because it was deleted" from "skipped for some other reason".
            LogAssert.Expect(LogType.Warning, new Regex("deleted while loading"));

            await NewService(backend).LoadAsync<TestSaveData>(Key);

            Assert.IsNull(backend.PeekPrimary(Key), "A deleted save must stay deleted.");
        });

        // --- Events -------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Recovery_RaisesOnlyOnSaveRecovered() => UniTask.ToCoroutine(async () =>
        {
            // A "Saving…" indicator lighting up in the middle of a LOAD would be a lie.
            var backend = CorruptPrimaryWithGoodBackup();
            var service = NewService(backend);
            var started = 0;
            var recovered = new List<SaveRecoveredEventArgs>();
            using var d1 = service.OnSaveStartedAsObservable.Subscribe(_ => started++);
            using var d2 = service.OnSaveRecoveredAsObservable.Subscribe(recovered.Add);
            LogAssert.ignoreFailingMessages = true;

            await service.LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(0, started, "No save event may fire during a load.");
            Assert.AreEqual(1, recovered.Count);
            Assert.AreEqual(Key, recovered[0].Key, "The event carries the LOGICAL key, not a decorated one.");
            Assert.IsTrue(recovered[0].PrimaryRepaired);
        });

        [UnityTest]
        public IEnumerator SkippedRepair_ReportsPrimaryRepairedFalse() => UniTask.ToCoroutine(async () =>
        {
            // Otherwise a consumer UI would announce a repair that never happened.
            var backend = CorruptPrimaryWithGoodBackup();
            backend.OnReadBackup = () => backend.SeedPrimary(Key, CurrentEnvelope(Sample()));
            var service = NewService(backend);
            var recovered = new List<SaveRecoveredEventArgs>();
            using var d = service.OnSaveRecoveredAsObservable.Subscribe(recovered.Add);
            LogAssert.ignoreFailingMessages = true;

            await service.LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(1, recovered.Count);
            Assert.IsFalse(recovered[0].PrimaryRepaired, "Repair skipped, so it must not claim otherwise.");
        });

        [UnityTest]
        public IEnumerator RepairTakesTheGateItself_AndDoesNotDeadlock() => UniTask.ToCoroutine(async () =>
        {
            // Repair runs inside a load, which deliberately holds no gate. Calling the public
            // SaveAsync from there would re-enter a non-reentrant SemaphoreSlim on the same key and
            // hang forever, so this asserts on COMPLETION within a bound rather than on "no throw" —
            // a deadlock throws nothing.
            var backend = CorruptPrimaryWithGoodBackup();
            LogAssert.ignoreFailingMessages = true;

            var load = NewService(backend).LoadAsync<TestSaveData>(Key);
            var timeout = UniTask.Delay(2000, DelayType.Realtime);
            var (winner, _, _) = await UniTask.WhenAny(load, timeout.ContinueWith(() => (TestSaveData)null));

            Assert.AreEqual(0, winner, "The load must complete; a deadlocked repair would simply never return.");
        });
    }
}
