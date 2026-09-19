using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;
using StubMigration = Sinkii09.UIFramework.Tests.SaveMigrationRegistryTests.StubMigration;

namespace Sinkii09.UIFramework.Tests
{
    // Slot decoration: isolation between slots, backward compatibility of slot 0, and the rule that
    // the decoration never reaches anything but storage.
    public class SaveSlotTests
    {
        private const string Key = "SlotTestKey";

        private static JsonSaveService ServiceWith(FakeStorageBackend backend, ISaveSlotContext slots)
            => new(backend, SaveMigrationRegistry.Empty, slots);

        [UnityTest]
        public IEnumerator Slot0_ReadsASavePreDatingSlots() => UniTask.ToCoroutine(async () =>
        {
            // The compatibility guarantee: slot 0 writes the undecorated key, so every save written
            // before slots existed IS slot 0 and needs no migration of any kind.
            var backend = new FakeStorageBackend();
            backend.SeedPrimary(Key, CurrentEnvelope(Sample()));

            var loaded = await ServiceWith(backend, new SaveSlotContext()).LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(42, loaded.Score);
        });

        [UnityTest]
        public IEnumerator Slots_AreIsolatedFromEachOther() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            var slots = new SaveSlotContext();
            var service = ServiceWith(backend, slots);

            slots.ActiveSlot = 0;
            await service.SaveAsync(Key, new TestSaveData { Name = "zero", Score = 1 });

            slots.ActiveSlot = 2;
            await service.SaveAsync(Key, new TestSaveData { Name = "two", Score = 2 });

            slots.ActiveSlot = 0;
            Assert.AreEqual("zero", (await service.LoadAsync<TestSaveData>(Key)).Name);

            slots.ActiveSlot = 2;
            Assert.AreEqual("two", (await service.LoadAsync<TestSaveData>(Key)).Name);
        });

        [UnityTest]
        public IEnumerator Slot_DecoratesOnlyTheStorageKey() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            var slots = new SaveSlotContext { ActiveSlot = 3 };

            await ServiceWith(backend, slots).SaveAsync(Key, Sample());

            Assert.IsNull(backend.PeekPrimary(Key), "Slot 3 must not write the undecorated key.");
            Assert.IsNotNull(backend.PeekPrimary(Key + ".s3"), "Slot 3 writes {key}.s3.");
        });

        [UnityTest]
        public IEnumerator ExistsAndDelete_RespectTheActiveSlot() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FakeStorageBackend();
            var slots = new SaveSlotContext();
            var service = ServiceWith(backend, slots);

            slots.ActiveSlot = 1;
            await service.SaveAsync(Key, Sample());

            slots.ActiveSlot = 0;
            Assert.IsFalse(await service.ExistsAsync(Key), "Slot 0 must not see slot 1's save.");
            Assert.IsFalse(await service.DeleteAsync(Key), "Deleting slot 0 must not touch slot 1.");

            slots.ActiveSlot = 1;
            Assert.IsTrue(await service.ExistsAsync(Key));
            Assert.IsTrue(await service.DeleteAsync(Key));
        });

        [UnityTest]
        public IEnumerator GenericOverloads_RespectTheActiveSlot() => UniTask.ToCoroutine(async () =>
        {
            // The reason slot resolution had to live INSIDE the service: the generic overloads resolve
            // their key from typeof(T).Name internally, so a wrapper above ISaveService could never
            // have covered them and a game mixing both styles would silently read the wrong slot.
            var backend = new FakeStorageBackend();
            var slots = new SaveSlotContext();
            var service = ServiceWith(backend, slots);

            slots.ActiveSlot = 4;
            await service.SaveAsync(new TestSaveData { Name = "slot-four", Score = 4 });

            slots.ActiveSlot = 0;
            Assert.IsNull(await service.LoadAsync<TestSaveData>(), "Generic load must honour the slot too.");

            slots.ActiveSlot = 4;
            Assert.AreEqual("slot-four", (await service.LoadAsync<TestSaveData>()).Name);
        });

        // --- The decoration must stay unreachable from user input ---------------------------------

        [UnityTest]
        public IEnumerator UserKeyContainingTheSlotSeparator_IsRejected() => UniTask.ToCoroutine(async () =>
        {
            // This is what makes the encoding collision-proof, and it is the assertion that matters:
            // if '.' were ever allowed through the strict validator, a game could name a key that
            // aliases another key's slot — and '..' would reach Path.Combine in the backend.
            var service = ServiceWith(new FakeStorageBackend(), new SaveSlotContext());

            foreach (var hostile in new[] { "a.s1", "..", "../escape", "a.b" })
            {
                var error = await CaptureAsync(async () => await service.SaveAsync(hostile, Sample()));
                Assert.IsInstanceOf<ArgumentException>(error, $"'{hostile}' must be rejected as a user key.");
            }
        });

        [Test]
        public void StorageKeyFor_RejectsAnOutOfRangeSlot()
        {
            // ISaveSlotContext is a public interface, so the value arriving at decoration has passed
            // through code this package does not control. An unbounded slot would also leak entries
            // into the gate dictionary, which is deliberately never cleared.
            Assert.Throws<ArgumentOutOfRangeException>(() => SaveKeyResolver.StorageKeyFor(Key, -1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SaveKeyResolver.StorageKeyFor(Key, SaveKeyResolver.MaxSlotIndex + 1));
        }

        [Test]
        public void SaveSlotContext_RefusesAnOutOfRangeSlotAndStaysPut()
        {
            // Refused rather than clamped: silently writing to a neighbouring slot would be worse than
            // ignoring the request. Logged rather than thrown because the value usually comes from UI.
            var slots = new SaveSlotContext { ActiveSlot = 2 };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("out of range"));
            slots.ActiveSlot = 99;

            Assert.AreEqual(2, slots.ActiveSlot);
        }

        [Test]
        public void ContainerBuilder_Exists_MustMatchInterfaceRegistrations()
        {
            // The framework only registers its default slot context when a game has not supplied one.
            // That guard compares ImplementationType unless includeInterfaceTypes is set
            // (ContainerBuilder.cs:118-120), so a game registering Register<ISaveSlotContext, MyImpl>()
            // would go undetected, the default would be registered after it, last-wins would hand the
            // service the FRAMEWORK's slot-0 instance, and every slot would silently collapse onto
            // slot 0 while every other test in this fixture still passed.
            var builder = new ContainerBuilder();
            builder.Register<ISaveSlotContext, SaveSlotContext>(Lifetime.Singleton);

            Assert.IsFalse(builder.Exists(typeof(ISaveSlotContext)),
                "Precondition: the default overload genuinely does not see an interface registration.");
            Assert.IsTrue(builder.Exists(typeof(ISaveSlotContext), includeInterfaceTypes: true),
                "The guard must use includeInterfaceTypes, or a game-supplied slot context is overwritten.");
        }

        // --- The logical key must keep reaching the codec -----------------------------------------

        [UnityTest]
        public IEnumerator MigrationChain_RunsOnEverySlot_NotJustSlotZero() => UniTask.ToCoroutine(async () =>
        {
            // Regression net for the worst trap in this phase. Migration chains are registered against
            // the key the GAME knows, so if the slot-decorated key were handed to the codec, key
            // "SlotTestKey.s2" would find no chain: slot 2 would sit at the base version forever,
            // never migrating, and never able to raise SaveSchemaVersionException when a newer build
            // wrote it. Everything else in this fixture would still pass.
            var backend = new FakeStorageBackend();
            var registry = new SaveMigrationRegistry();
            registry.Register(Key, new StubMigration(1, token =>
            {
                token["Score"] = token["Score"].Value<int>() + 100;
                return token;
            }));

            var slots = new SaveSlotContext { ActiveSlot = 2 };
            backend.SeedPrimary(Key + ".s2", Envelope(Sample(), 1));

            var service = new JsonSaveService(backend, registry, slots);
            var loaded = await service.LoadAsync<TestSaveData>(Key);

            Assert.AreEqual(142, loaded.Score, "The chain must run for slot 2 exactly as it does for slot 0.");

            // ...and the stamped version must be the chain's, not the base version.
            await service.SaveAsync(Key, loaded);
            Assert.AreEqual(2, JObject.Parse(backend.PeekPrimary(Key + ".s2"))["SchemaVersion"].Value<int>());
        });
    }
}
