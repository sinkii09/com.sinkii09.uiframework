using NUnit.Framework;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework.Tests
{
    // Guards the SHAPE of the loader registration, which is the part a later refactor breaks
    // silently. IAssetLoader sits beside IUILoader on ONE object on purpose: an Addressables key
    // owns exactly one handle, so two loader objects would keep two ref-count ledgers for the same
    // key and the second UnloadAsync would release something already released.
    //
    // Splitting that into two registrations would still compile, still resolve, and still pass every
    // other test in this suite. That is exactly why it is asserted here.
    //
    // NOT covered here, deliberately: loading a real asset. This package ships no Resources folder
    // and must not start shipping one — assets under Resources/ are pulled into every consuming
    // game's build regardless of the test asmdef's defineConstraints, so a fixture asset would cost
    // every consumer bytes forever. Resources.LoadAsync also needs the player loop, which EditMode
    // does not pump. The happy path belongs to an integration test in a consuming project; what is
    // left in the loaders themselves is guard clauses.
    public class AssetLoaderRegistrationTests
    {
        [Test]
        public void ResourcesLoader_SatisfiesBothLoaderInterfaces()
        {
            Assert.That(typeof(IUILoader).IsAssignableFrom(typeof(ResourcesUILoader)), Is.True);
            Assert.That(typeof(IAssetLoader).IsAssignableFrom(typeof(ResourcesUILoader)), Is.True);
        }

        [Test]
        public void AddressablesLoader_SatisfiesBothLoaderInterfaces()
        {
            Assert.That(typeof(IUILoader).IsAssignableFrom(typeof(AddressablesUILoader)), Is.True);
            Assert.That(typeof(IAssetLoader).IsAssignableFrom(typeof(AddressablesUILoader)), Is.True);
        }

        [Test]
        public void ResourcesMode_ResolvesOneInstanceForBothInterfaces()
        {
            using var container = BuildLoaderContainer(LoaderMode.Resources);

            var viewLoader = container.Resolve<IUILoader>();
            var assetLoader = container.Resolve<IAssetLoader>();

            Assert.That(viewLoader, Is.InstanceOf<ResourcesUILoader>());
            Assert.That(assetLoader, Is.SameAs(viewLoader),
                "Two instances means two handle ledgers for one key — the failure this shape exists to prevent.");
        }

        [Test]
        public void AddressablesMode_ResolvesOneInstanceForBothInterfaces()
        {
            using var container = BuildLoaderContainer(LoaderMode.Addressables);

            var viewLoader = container.Resolve<IUILoader>();
            var assetLoader = container.Resolve<IAssetLoader>();

            Assert.That(viewLoader, Is.InstanceOf<AddressablesUILoader>());
            Assert.That(assetLoader, Is.SameAs(viewLoader),
                "Addressables is the mode where a duplicated ledger actually double-releases a handle.");
        }

        // Calls the PRODUCTION registration method rather than restating it. A test that re-declared
        // the registration would keep passing while production drifted away from it, which is the
        // only way this particular mistake could reach a consumer.
        private static IObjectResolver BuildLoaderContainer(LoaderMode mode)
        {
            var config = ScriptableObject.CreateInstance<UIFrameworkConfig>();
            config.LoaderMode = mode;

            var builder = new ContainerBuilder();
            UIFrameworkLifetimeScope.RegisterLoader(builder, config);
            var container = builder.Build();

            // RegisterLoader reads LoaderMode synchronously and keeps no reference to the config,
            // so the asset is dead weight from here on. Left alive it would leak one ScriptableObject
            // per test into the Editor session.
            Object.DestroyImmediate(config);
            return container;
        }
    }
}
