using System;

namespace Sinkii09.UIFramework
{
    // Reused across OnSaveStarted/OnSaveCompleted/OnSaveFailed — Error is null on Started/Completed,
    // set on Failed. One type covers all three observables (KISS, avoids near-identical arg types).
    public readonly struct SaveEventArgs
    {
        public readonly string Key;
        public readonly Exception Error;

        public SaveEventArgs(string key, Exception error)
        {
            Key = key;
            Error = error;
        }
    }
}
