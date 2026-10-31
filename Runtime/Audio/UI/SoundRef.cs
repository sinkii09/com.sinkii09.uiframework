using System;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>An inspector reference to a sound by its event path.</summary>
    /// <remarks>
    /// Stores the path, not the hash, so a broken reference can be named by the validator and rewritten
    /// by the rename tool. Game code uses the generated SoundIds constants instead.
    /// </remarks>
    [Serializable]
    public struct SoundRef
    {
        [SerializeField] private string _path;

        public SoundRef(string path) => _path = path;

        public string Path => _path;
        public bool IsEmpty => string.IsNullOrEmpty(_path);
        public SoundId Id => SoundId.FromPath(_path);
    }
}
