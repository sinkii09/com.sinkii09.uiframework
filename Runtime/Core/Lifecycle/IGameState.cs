namespace Sinkii09.UIFramework
{
    // PausesGameTime: advisory metadata. Implementations manage Time.timeScale
    // manually in OnEnterAsync/OnExitAsync using try/finally.
    public interface IGameState : IViewState
    {
        string SceneName { get; }
        bool PausesGameTime { get; }
    }
}
