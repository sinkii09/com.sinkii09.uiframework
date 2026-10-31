using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sinkii09.UIFramework.Tests.Editor
{
    public class BankManagerTests
    {
        private const string Key = "bank";

        private ControlledAssetLoader _loader;
        private RecordingBankSink _sink;
        private BankManager _banks;
        private SoundBank _bank;

        [SetUp]
        public void SetUp()
        {
            _loader = new ControlledAssetLoader();
            _sink = new RecordingBankSink();
            _banks = new BankManager(_loader, _sink);
            _bank = ScriptableObject.CreateInstance<SoundBank>();
        }

        [TearDown]
        public void TearDown()
        {
            _banks.Dispose();
            Object.DestroyImmediate(_bank);
        }

        [Test]
        public void DoubleLoad_SharesOneLoad_AndOneUnloadKeepsItRegistered()
        {
            var first = _banks.LoadAsync(Key);
            var second = _banks.LoadAsync(Key);
            _loader.Complete(Key, _bank);

            Assert.IsTrue(first.Result());
            Assert.IsTrue(second.Result());
            Assert.AreEqual(1, _loader.Loads);
            Assert.AreEqual(1, _sink.Registered.Count);

            _banks.Unload(Key);
            Assert.IsTrue(_banks.IsLoaded(Key));
            Assert.AreEqual(0, _sink.Unregistered.Count);

            _banks.Unload(Key);
            Assert.IsFalse(_banks.IsLoaded(Key));
            Assert.AreEqual(1, _sink.Unregistered.Count);
            CollectionAssert.AreEqual(new[] { Key }, _loader.Unloads);
        }

        [Test]
        public void UnloadDuringLoad_ReleasesWhenTheLoadLands_AndNeverRegisters()
        {
            var load = _banks.LoadAsync(Key);
            _banks.Unload(Key);
            _loader.Complete(Key, _bank);

            Assert.IsFalse(load.Result());
            Assert.AreEqual(0, _sink.Registered.Count);
            CollectionAssert.AreEqual(new[] { Key }, _loader.Unloads);
        }

        [Test]
        public void UnloadDuringLoad_ThenReload_JoinsTheSameLoad()
        {
            var stale = _banks.LoadAsync(Key);
            _banks.Unload(Key);
            var fresh = _banks.LoadAsync(Key);
            _loader.Complete(Key, _bank);

            Assert.IsTrue(fresh.Result());
            Assert.IsTrue(stale.Result(), "Same entry, so the original awaiter sees the completed load too.");
            Assert.AreEqual(1, _loader.Loads);
            Assert.AreEqual(0, _loader.Unloads.Count, "A stale load must never release the key the reload owns.");
            Assert.AreEqual(1, _banks.ReferenceCount(Key));
        }

        [Test]
        public void CallerCancels_DropsOnlyItsOwnReference()
        {
            using var cts = new CancellationTokenSource();
            var cancelled = _banks.LoadAsync(Key, cts.Token);
            var kept = _banks.LoadAsync(Key);
            cts.Cancel();
            _loader.Complete(Key, _bank);

            Assert.IsFalse(cancelled.Result());
            Assert.IsTrue(kept.Result(), "One caller cancelling must not fail the shared load.");
            Assert.AreEqual(1, _banks.ReferenceCount(Key));
        }

        [Test]
        public void Fault_ReturnsFalse_AndALaterUnloadIsANoOp()
        {
            var load = _banks.LoadAsync(Key);
            LogAssert.Expect(LogType.Error, new Regex("failed to load"));
            _loader.Fail(Key);

            Assert.IsFalse(load.Result());
            LogAssert.Expect(LogType.Warning, new Regex("no matching successful load"));
            _banks.Unload(Key);
            Assert.AreEqual(0, _banks.ReferenceCount(Key));
        }

        [Test]
        public void SynchronousLoader_KeepsTheBank()
        {
            _loader.SynchronousResult = _bank;
            Assert.IsTrue(_banks.LoadAsync(Key).Result());
            Assert.IsTrue(_banks.IsLoaded(Key));
        }

        [Test]
        public void DisposeMidLoad_LateCompletionReleasesItsHandle()
        {
            _loader.IgnoreCancellation = true;
            var load = _banks.LoadAsync(Key);
            _banks.Dispose();
            _loader.Complete(Key, _bank);

            Assert.IsFalse(load.Result());
            Assert.AreEqual(0, _sink.Registered.Count);
            CollectionAssert.AreEqual(new[] { Key }, _loader.Unloads);
        }
    }
}
