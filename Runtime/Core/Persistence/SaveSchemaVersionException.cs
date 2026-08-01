using System;

namespace Sinkii09.UIFramework
{
    // Thrown when a save file's schema version is newer than this build understands.
    //
    // Deliberately a dedicated type rather than NotSupportedException: LoadAsync treats this
    // exception as "do not fall back to the backup", and Newtonsoft raises NotSupportedException
    // internally for unrelated reasons — sharing the type would silently skip backup recovery for
    // a genuine corruption.
    public sealed class SaveSchemaVersionException : Exception
    {
        public string Key { get; }
        public int FileVersion { get; }
        public int SupportedVersion { get; }

        public SaveSchemaVersionException(string key, int fileVersion, int supportedVersion)
            : base($"Save '{key}' has schema version {fileVersion}, which is newer than this build supports ({supportedVersion}). It was most likely written by a newer version of the game.")
        {
            Key = key;
            FileVersion = fileVersion;
            SupportedVersion = supportedVersion;
        }
    }
}
