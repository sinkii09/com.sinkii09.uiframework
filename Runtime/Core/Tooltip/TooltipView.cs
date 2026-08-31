using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sinkii09.UIFramework
{
    // The built-in tooltip: title + icon + body + variable-length stat lines + footer, each
    // section optional. Renders TooltipContent and nothing else; a payload with any other
    // ViewKey routes to a project's own TooltipViewBase subclass instead.
    //
    // Registers under the empty key (leave _viewKey blank on the prefab).
    public class TooltipView : TooltipViewBase
    {
        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _body;
        [SerializeField] private TextMeshProUGUI _footer;

        [Header("Stat lines")]
        [SerializeField] private RectTransform _statsRoot;
        [SerializeField] private TooltipStatLineView _statLinePrefab;

        // Grown on demand and reused. Rows are deactivated rather than destroyed, so a tooltip
        // sweeping a grid of differently-sized payloads allocates only up to the largest one.
        private readonly List<TooltipStatLineView> _statRows = new();

        public override void Bind(ITooltipPayload payload)
        {
            // A non-TooltipContent payload reaching here means a ViewKey pointed at the built-in
            // view but the payload is not what it renders — a wiring mistake worth surfacing.
            if (payload is not TooltipContent content)
            {
                Debug.LogError(
                    $"[TooltipView] Expected a {nameof(TooltipContent)} payload but got " +
                    $"{payload?.GetType().Name ?? "null"}. Give the payload a ViewKey matching a " +
                    "custom TooltipViewBase, or return TooltipContent.", this);
                return;
            }

            SetText(_title, content.Title);
            SetText(_body, content.Body);
            SetText(_footer, content.Footer);

            if (_icon != null)
            {
                _icon.sprite = content.Icon;
                _icon.gameObject.SetActive(content.Icon != null);
            }

            BindStats(content.Stats);
        }

        private void BindStats(IReadOnlyList<TooltipStatLine> stats)
        {
            if (_statsRoot == null || _statLinePrefab == null)
                return;

            int count = stats?.Count ?? 0;

            for (int i = _statRows.Count; i < count; i++)
            {
                var row = Instantiate(_statLinePrefab, _statsRoot);
                // Rows are created after Awake's sweep, so they would otherwise keep their
                // prefab's raycastTarget and put the tooltip back under the cursor.
                StripRaycasts(row.gameObject);
                _statRows.Add(row);
            }

            for (int i = 0; i < _statRows.Count; i++)
            {
                var row = _statRows[i];
                if (row == null) continue;   // Unity fake-null: a row destroyed out from under us

                bool used = i < count;
                row.gameObject.SetActive(used);
                if (used) row.Set(stats[i]);
            }

            _statsRoot.gameObject.SetActive(count > 0);
        }

        private static void SetText(TextMeshProUGUI target, string value)
        {
            if (target == null) return;
            target.text = value;
            // Hide the whole section rather than leaving an empty line that a VerticalLayoutGroup
            // would still allocate spacing for.
            target.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }
    }
}
