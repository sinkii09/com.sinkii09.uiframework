namespace Sinkii09.UIFramework
{
    public interface ILoadingContext
    {
        string TargetScene { get; }
        void Set(string sceneName);
        // Called by LoadingState after consuming to prevent stale re-use.
        void Reset();
    }
}
