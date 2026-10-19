using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// The full-screen Image a pattern wipe is drawn on (UIPatternWipe shader). Owns a per-instance copy of
    /// the wipe material, so <see cref="PatternWipeTransition"/> can drive Cover/Reveal without dirtying the
    /// shared asset.
    ///
    /// <para>The Image is the curtain (its sprite × colour). It must be Type Simple, stretched over the whole
    /// screen, have no sprite or one covering its whole texture (not atlased), and not sit under a Mask —
    /// see the shader header.</para>
    /// </summary>
    [RequireComponent(typeof(Image))]
    public sealed class PatternWipeSurface : MonoBehaviour
    {
        private static readonly int CoverId = Shader.PropertyToID("_Cover");
        private static readonly int RevealId = Shader.PropertyToID("_Reveal");

        private Material _instance;
        private bool _failed;   // reported once; SetPose runs every tween frame

        private void Awake() => EnsureMaterial();

        /// <summary>
        /// <paramref name="cover"/>: 0..1 how far the tiles have covered the screen.
        /// <paramref name="reveal"/>: 0..1 how far they have cleared it again (with cover at 1).
        /// </summary>
        public void SetPose(float cover, float reveal)
        {
            Material material = EnsureMaterial();
            if (material == null) return;

            material.SetFloat(CoverId, cover);
            material.SetFloat(RevealId, reveal);
        }

        // One entry point for Awake and SetPose: a transition may run before this object's Awake (an
        // inactive overlay), and Awake must not replace an instance a transition already posed.
        private Material EnsureMaterial()
        {
            if (_instance != null || _failed) return _instance;

            var image = GetComponent<Image>();
            Material shared = image.material;
            if (shared == null || !shared.HasProperty(CoverId) || !shared.HasProperty(RevealId))
            {
                _failed = true;
                Debug.LogError($"[PatternWipe] '{name}' needs a material using the UIFramework/UI/Pattern Wipe shader on its Image.", this);
                return null;
            }

            Sprite sprite = image.sprite;
            if (image.preserveAspect || (sprite != null && (sprite.packed || sprite.rect.width != sprite.texture.width || sprite.rect.height != sprite.texture.height)))
                Debug.LogWarning($"[PatternWipe] '{name}': the sprite must cover its whole texture (not atlased) and Preserve Aspect must be off, or the tile grid is distorted.", this);

            _instance = new Material(shared) { name = shared.name + " (Instance)" };
            image.material = _instance;
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance != null) Destroy(_instance);
        }
    }
}
