namespace Sinkii09.UIFramework
{
    public interface IUIPoolable
    {
        void OnSpawnedFromPool();
        void OnReturnedToPool();
    }
}
