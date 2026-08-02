namespace Sinkii09.UIFramework
{
    public sealed class LoadingContext : ILoadingContext
    {
        public string TargetScene { get; private set; }

        public void Set(string sceneName)
        {
            TargetScene = sceneName;
        }

        public void Reset()
        {
            TargetScene = null;
        }
    }
}
