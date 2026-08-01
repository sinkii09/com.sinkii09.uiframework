using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;

namespace Sinkii09.UIFramework.Tests
{
    // Shared fixtures for the save/load tests. Kept in one place so the three test files stay under
    // the 200-line limit without duplicating the envelope builder or the async-throw helper.
    internal static class SaveTestHelpers
    {
        // Any version above the current one — stands in for "written by a newer build of the game".
        internal static int NewerVersion => SaveEnvelopeCodec.CurrentSchemaVersion + 1;

        internal static string Envelope(TestSaveData data, int schemaVersion)
            => JsonConvert.SerializeObject(
                new SaveEnvelope<TestSaveData> { SchemaVersion = schemaVersion, Data = data });

        internal static string CurrentEnvelope(TestSaveData data)
            => Envelope(data, SaveEnvelopeCodec.CurrentSchemaVersion);

        internal static TestSaveData Sample() => new()
        {
            Name = "player-one",
            Score = 42,
            Inventory = new Dictionary<string, int> { ["potion"] = 3, ["ether"] = 1 }
        };

        // NUnit's Assert.ThrowsAsync only understands Task, not UniTask, so exceptions are captured
        // explicitly instead.
        internal static async UniTask<Exception> CaptureAsync(Func<UniTask> operation)
        {
            try
            {
                await operation();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
