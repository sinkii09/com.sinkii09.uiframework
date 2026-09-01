using System;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Marks a serialized field as legitimately allowed to be null, exempting it from
    /// <see cref="UIViewValidator"/>'s unassigned-reference check. Applies to any
    /// <see cref="UnityEngine.MonoBehaviour"/> the validator is run against, not just views.
    ///
    /// Use this only where null is a MEANINGFUL value, not merely a tolerated one — an optional
    /// transition, an icon that some skins omit. A field that is "usually assigned" is not
    /// optional; leaving it unmarked is what makes the missing-reference report useful.
    /// </summary>
    // Inherited is not set: it has no meaning for fields, which cannot be overridden.
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public sealed class UIOptionalAttribute : Attribute
    {
    }
}
