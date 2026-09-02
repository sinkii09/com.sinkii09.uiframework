using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// What a toast displays. An immutable value type, deliberately.
    ///
    /// <para>Melvor reuses one mutable payload instance per notification and gets away with it only
    /// because it renders synchronously. A shared instance published through a coalesced observable
    /// is read at flush time — after it has already been mutated for the NEXT notification. Pooling
    /// has the same defect from the other end: an instance released on dismiss re-aliases if a
    /// later flush still holds it. Copying a struct makes both unrepresentable.</para>
    ///
    /// <para><see cref="Title"/> and <see cref="Body"/> are expected to be already localized by the
    /// caller; nothing re-localizes them if the language changes while a toast is up.</para>
    /// </summary>
    public readonly struct NotificationContent
    {
        public readonly string Title;
        public readonly string Body;
        public readonly Sprite Icon;

        /// <summary>Accumulates across merges — "Iron Ore x5" rather than five separate toasts.</summary>
        public readonly int Quantity;

        public readonly NotificationPriority Priority;

        /// <summary>Seconds on screen. 0 or less means "use the configured default".</summary>
        public readonly float DurationSeconds;

        public NotificationContent(string title, string body = null, Sprite icon = null,
            int quantity = 1, NotificationPriority priority = NotificationPriority.Normal,
            float durationSeconds = 0f)
        {
            Title = title;
            Body = body;
            Icon = icon;
            Quantity = quantity;
            Priority = priority;
            DurationSeconds = durationSeconds;
        }

        // Used when a merge folds an incoming request into a live entry: quantity accumulates and
        // priority only ever rises, so a Normal follow-up can never demote an Error already shown.
        internal NotificationContent MergedWith(in NotificationContent incoming)
            => new NotificationContent(
                incoming.Title ?? Title,
                incoming.Body ?? Body,
                incoming.Icon != null ? incoming.Icon : Icon,
                Quantity + incoming.Quantity,
                (NotificationPriority)Mathf.Max((int)Priority, (int)incoming.Priority),
                incoming.DurationSeconds > 0f ? incoming.DurationSeconds : DurationSeconds);
    }
}
