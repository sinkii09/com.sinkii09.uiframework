using TMPro;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    // One "Damage    12-18" row inside the built-in TooltipView. Pooled by TooltipView, never
    // created directly — assign it as that view's stat-line prefab.
    public class TooltipStatLineView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private TextMeshProUGUI _value;

        public void Set(in TooltipStatLine line)
        {
            if (_label != null) _label.text = line.Label;
            if (_value != null) _value.text = line.Value;
        }
    }
}
