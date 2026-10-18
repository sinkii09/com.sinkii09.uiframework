using UnityEngine;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>Holds a <see cref="RecyclerViewSettings"/> so a SerializedObject can walk it the way the Inspector does.</summary>
    public sealed class RecyclerViewSettingsHost : ScriptableObject
    {
        public RecyclerViewSettings Settings = new RecyclerViewSettings();
    }
}
