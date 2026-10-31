using UnityEditorInternal;
using UnityEngine;

namespace Sinkii09.UIFramework.Editor
{
    // Put one at a game's root folder. Every SoundEvent under that folder becomes a constant in
    // <TargetAssembly folder>/Generated/<ClassName>.g.cs, so ids live in the game's assembly, never in
    // the package. Editor-only: nothing here reaches a player.
    [CreateAssetMenu(menuName = "UIFramework/Audio/Sound Ids Codegen Settings", fileName = "SoundIdsCodegenSettings")]
    public sealed class SoundIdsCodegenSettings : ScriptableObject
    {
        [Tooltip("The game assembly that receives the generated class. It must reference Sinkii09.UIFramework.")]
        public AssemblyDefinitionAsset TargetAssembly;

        public string ClassName = "SoundIds";

        [Tooltip("Empty uses the assembly's root namespace.")]
        public string Namespace;
    }
}
