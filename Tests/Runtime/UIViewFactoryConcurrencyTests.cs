using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the C1 finding: UIViewFactory's in-flight dedup guard previously only
    // covered the generic CreateAsync path — the type-erased overload (what UINavigator's
    // auto-registration path actually calls for every view) had no guard at all, so two concurrent
    // requests for the same view type instantiated it twice. Phase 01 consolidated both public
    // overloads onto one CreateCoreAsync so the guard now covers all three entry points.
    // See plans/260801-2148-correctness-cluster/phase-01-factory-consolidation.md.
    public class UIViewFactoryConcurrencyTests
    {
        private readonly GameObjectTracker _gos = new();

        [TearDown]
        public void TearDown() => _gos.DestroyAll();

        [UnityTest]
        public IEnumerator CreateAsync_TypeErased_ConcurrentCalls_InstantiatesOnce() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeUILoader(_gos) { LoadDelayFrames = 2 };
            loader.RegisterPrefab<TestView>("TestView");
            var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);

            // C1-1: the C1 regression — two un-awaited type-erased calls for one view type must
            // dedup to a single load/instance instead of racing two separate instantiations.
            var task1 = factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView");
            var task2 = factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView");
            var view1 = await task1;
            var view2 = (TestView)await task2;
            _gos.Track(view2);

            Assert.AreSame(view1, view2);
            Assert.AreEqual(1, loader.LoadCount);
        });

        [UnityTest]
        public IEnumerator CreateAsync_GenericAndTypeErased_Concurrent_ShareOneInstance() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeUILoader(_gos) { LoadDelayFrames = 2 };
            loader.RegisterPrefab<TestView>("TestView");
            var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);

            // C1-2: cross-path dedup — before Phase 01 the generic and type-erased overloads had
            // separate creation logic, so a generic call and a type-erased call for the same type
            // still raced each other even after the generic-only guard was added.
            var genericTask = factory.CreateAsync<TestView, TestViewModel>();
            var erasedTask = factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView");
            var fromGeneric = await genericTask;
            _gos.Track(fromGeneric);
            var fromErased = await erasedTask;

            Assert.AreSame(fromGeneric, fromErased);
            Assert.AreEqual(1, loader.LoadCount);
        });

        [UnityTest]
        public IEnumerator CreateAsync_TypeErased_ViewModelResolvesConcreteViewType() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeUILoader(_gos);
            loader.RegisterPrefab<TestViewInjectingSelf>("TestViewInjectingSelf");
            var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);

            // C1-3: RegisterInstance(instance, viewType) must bind under the CONCRETE view type —
            // a ViewModel that injects the concrete view (TestViewInjectingSelf, not UIViewBase)
            // must resolve it regardless of which CreateAsync overload created the pair.
            var view = (TestViewInjectingSelf)await factory.CreateAsync(
                typeof(TestViewInjectingSelf), typeof(TestViewModelInjectingView), "TestViewInjectingSelf");
            _gos.Track(view);

            Assert.AreSame(view, view.VM.InjectedView);
        });

        [UnityTest]
        public IEnumerator CreateAsync_LoadFails_ClearsPendingAndAllowsRetry() => UniTask.ToCoroutine(async () =>
        {
            var loadError = new InvalidOperationException("boom");
            var loader = new FakeUILoader(_gos) { LoadError = loadError };
            loader.RegisterPrefab<TestView>("TestView");
            var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);

            var error = await CaptureAsync(async () =>
                await factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView"));
            Assert.AreSame(loadError, error);

            // C1-4: finally { _pending.Remove(viewType) } — a failed create must not poison the
            // type forever; the very next call for the same type must be a normal retry, not a
            // permanent "always awaiting the failed in-flight task" deadlock.
            loader.LoadError = null;
            var view = (TestView)await factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView");
            _gos.Track(view);

            Assert.IsNotNull(view);
            Assert.AreEqual(2, loader.LoadCount);
        });

        [UnityTest]
        public IEnumerator CreateAsync_CachedInstance_DoesNotReloadOrReparent() => UniTask.ToCoroutine(async () =>
        {
            var loader = new FakeUILoader(_gos);
            loader.RegisterPrefab<TestView>("TestView");
            var factory = new UIViewFactory(loader, UITestHelpers.BuildContainer(), null);

            var view1 = (TestView)await factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView");
            _gos.Track(view1);
            var view2 = await factory.CreateAsync(typeof(TestView), typeof(TestViewModel), "TestView");

            // C1-5: the cache-hit path (isNew == false) survived the CreateCoreAsync merge — a
            // second request for an already-cached type must reuse it, not reload/reinstantiate.
            Assert.AreSame(view1, view2);
            Assert.AreEqual(1, loader.LoadCount);
        });
    }
}
