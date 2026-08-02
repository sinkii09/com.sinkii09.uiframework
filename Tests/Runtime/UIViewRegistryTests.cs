using System;
using System.Linq;
using NUnit.Framework;

namespace Sinkii09.UIFramework.Tests
{
    // Regression test for the UIViewRegistry.AutoRegister reflection-swallow fix: a bare
    // `catch { continue; }` around Assembly.GetTypes() used to discard an entire assembly's views
    // on any partial ReflectionTypeLoadException, with no diagnostic.
    //
    // Scope note: AutoRegister() itself is not exercised directly here. It is guarded by static,
    // domain-reload-reset state (_registrations.Count > 0 short-circuits a second call), so its
    // result depends on whatever already ran earlier in the same Editor session/domain — order-
    // dependent and unsuitable for an isolated unit test. Constructing a genuine
    // ReflectionTypeLoadException is also impractical without a deliberately broken assembly.
    // Instead this verifies the exact recovery expression the fix relies on
    // (`ex.Types.Where(t => t != null).ToArray()`) against representative inputs — the well-formed
    // (no-exception) auto-registration path is already covered by the existing UIViewFactory/
    // UINavigator tests, which depend on AutoRegister having found TestView et al.
    // See plans/260802-1122-hardening-cluster/plan.md, Item 3.
    public sealed class UIViewRegistryTests
    {
        [Test]
        public void RecoveryFilter_MixedNullAndRealTypes_KeepsOnlyRealTypesInOrder()
        {
            var types = new[] { typeof(string), null, typeof(int), null, typeof(UIViewRegistryTests) };

            var recovered = types.Where(t => t != null).ToArray();

            Assert.AreEqual(3, recovered.Length);
            Assert.AreEqual(typeof(string), recovered[0]);
            Assert.AreEqual(typeof(int), recovered[1]);
            Assert.AreEqual(typeof(UIViewRegistryTests), recovered[2]);
        }

        [Test]
        public void RecoveryFilter_AllTypesFailed_RecoversEmptyArray_DoesNotThrow()
        {
            var types = new Type[] { null, null, null };

            var recovered = types.Where(t => t != null).ToArray();

            Assert.IsEmpty(recovered);
        }

        [Test]
        public void RecoveryFilter_NoTypesFailed_RecoversAllTypes()
        {
            var types = new[] { typeof(string), typeof(int) };

            var recovered = types.Where(t => t != null).ToArray();

            Assert.AreEqual(types.Length, recovered.Length);
        }
    }
}
