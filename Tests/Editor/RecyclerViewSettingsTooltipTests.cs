using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// The Settings block shows only group headers; what each number does is in its hover tooltip.
    /// Read through <c>SerializedProperty.tooltip</c> — what the Inspector itself shows — so a field
    /// added without a tooltip fails here instead of shipping with no explanation at all.
    /// </summary>
    public class RecyclerViewSettingsTooltipTests
    {
        [Test]
        public void SerializedFields_AllHaveTooltip_SoEveryFieldExplainsItselfOnHover()
        {
            var host = ScriptableObject.CreateInstance<RecyclerViewSettingsHost>();
            try
            {
                using var serialized = new SerializedObject(host);
                SerializedProperty settings = serialized.FindProperty(nameof(RecyclerViewSettingsHost.Settings));

                var visited = new List<string>();
                var blank = new List<string>();
                SerializedProperty iterator = settings.Copy();
                SerializedProperty end = settings.GetEndProperty();
                for (bool enterChildren = true;
                     iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end);
                     enterChildren = false)
                {
                    visited.Add(iterator.name);
                    if (string.IsNullOrWhiteSpace(iterator.tooltip)) blank.Add(iterator.name);
                }

                string[] declared = typeof(RecyclerViewSettings)
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(field => field.IsDefined(typeof(SerializeField), false))
                    .Select(field => field.Name)
                    .ToArray();

                Assert.That(visited, Is.EquivalentTo(declared), "the Inspector walk must reach every serialized field");
                Assert.That(blank, Is.Empty, "fields without a hover tooltip");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
