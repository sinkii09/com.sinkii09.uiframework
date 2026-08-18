using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Sinkii09.UIFramework;

namespace Sinkii09.UIFramework.Tests.Editor
{
    /// <summary>
    /// Guards a hazard that produces no compiler diagnostic at all.
    ///
    /// <para><c>UIControlBase</c> declares <c>Awake</c> and <c>OnDestroy</c> as <b>private</b>, so a
    /// subclass declaring its own is perfectly legal C# — no override, no <c>new</c> warning, no
    /// error. Unity's message dispatch then calls only the most-derived one, silently skipping the
    /// base's CanvasGroup caching and its <c>OnInitialize()</c> call. The control simply never
    /// initializes, and nothing anywhere reports why.</para>
    ///
    /// <para>"Don't declare Awake" is a convention no tool enforces, so enforce it here.</para>
    /// </summary>
    public class UIControlBaseLifecycleTests
    {
        private static readonly string[] SealedMessages = { "Awake", "OnDestroy" };

        private const BindingFlags Declared =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        [Test]
        public void NoUIControlSubclass_ShadowsTheBaseLifecycleMessages()
        {
            var offenders = new List<string>();

            foreach (System.Type type in typeof(UIControlBase).Assembly.GetTypes())
            {
                if (!type.IsSubclassOf(typeof(UIControlBase))) continue;

                foreach (string message in SealedMessages)
                {
                    if (type.GetMethod(message, Declared) != null)
                        offenders.Add($"{type.FullName}.{message}()");
                }
            }

            Assert.IsEmpty(offenders,
                "These types shadow a UIControlBase lifecycle message, so Unity will never run the " +
                "base implementation and OnInitialize()/OnDispose() will silently not fire. Move the " +
                "logic into OnInitialize/OnDispose instead:\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void UIControlBase_StillDeclaresTheMessagesPrivately()
        {
            // If these ever become protected/virtual the hazard above disappears and this guard can
            // go — but until then, the test above is only meaningful while they are private.
            foreach (string message in SealedMessages)
            {
                MethodInfo method = typeof(UIControlBase).GetMethod(message, Declared);

                Assert.IsNotNull(method, $"UIControlBase no longer declares {message}()");
                Assert.IsTrue(method.IsPrivate,
                    $"UIControlBase.{message}() is no longer private — revisit the shadowing guard");
            }
        }

        [Test]
        public void RecyclerView_IsAUIControl()
        {
            Assert.IsTrue(typeof(RecyclerView).IsSubclassOf(typeof(UIControlBase)),
                "RecyclerView relies on UIControlBase's OnDispose to tear its pool down");
        }
    }
}
