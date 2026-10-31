namespace Sinkii09.UIFramework
{
    // The fixed bus set. Master scales every other bus; there is no deeper hierarchy in v1. Bus gain
    // is computed by the engine, not an AudioMixer: WebGL only supports group volume there, and the
    // draft that used both applied the bus gain twice.
    public enum AudioBus
    {
        Master = 0,
        Music = 1,
        Sfx = 2,
        Ui = 3,
    }
}
