using Cysharp.Threading.Tasks;
using System.Threading;

namespace Sinkii09.UIFramework
{
    public interface IUIObjectPool
    {
        UniTask<T> GetAsync<T>(string key, CancellationToken ct = default) where T : IUIView, IUIPoolable;
        void Return<T>(T instance) where T : IUIView, IUIPoolable;
        UniTask PreloadAsync<T>(string key, int count, CancellationToken ct = default) where T : IUIView, IUIPoolable;
    }
}
