namespace Sinkii09.UIFramework
{
    // How an event with several clips picks one per play.
    public enum SoundContainerMode
    {
        Single = 0,
        // Uniform pick that never repeats any of the last AvoidRepeat picks.
        Random = 1,
        // Every clip once in shuffled order, then reshuffle; never repeats across a reshuffle.
        Shuffle = 2,
    }
}
