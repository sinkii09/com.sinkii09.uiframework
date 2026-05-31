using R3;

namespace Sinkii09.UIFramework
{
    public interface IUIEventBus
    {
        void Publish<T>(T evt) where T : IUIEvent;
        Observable<T> Receive<T>() where T : IUIEvent;
    }
}
