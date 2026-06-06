using Cysharp.Threading.Tasks;
using System;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUIViewFactory
    {
        UniTask<TView> CreateAsync<TView, TViewModel>(CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel;

        UniTask<TView> CreateAsync<TView, TViewModel, TArgs>(TArgs args, CancellationToken ct = default)
            where TView : IUIView
            where TViewModel : class, IViewModel<TArgs>
            where TArgs : IViewArgs;

        // Type-erased overload used by UINavigator's auto-registration path — AOT-safe, no MakeGenericMethod.
        // key: addressable/Resources key for the prefab (from UIViewKeyAttribute or view type name).
        UniTask<IUIView> CreateAsync(Type viewType, Type vmType, string key, CancellationToken ct = default);
    }
}
