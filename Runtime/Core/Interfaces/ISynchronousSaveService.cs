namespace Sinkii09.UIFramework
{
    // Optional capability: serialize and land a payload without awaiting.
    //
    // A separate interface rather than a member on ISaveService. Adding one there breaks every
    // implementer and forces a major version — the same reasoning that keeps
    // JsonSaveService.OnSaveRecoveredAsObservable off that interface — and this capability really is
    // optional: a service over a network backend cannot offer it at all.
    //
    // AutoSaveScheduler probes for this with a type test on whatever ISaveService resolves to, so a
    // game that substitutes its own save service keeps a working autosave either way: with this
    // interface the pause flush lands synchronously, without it the scheduler falls back to firing
    // the async save and accepts that an OS kill may beat it.
    public interface ISynchronousSaveService
    {
        /// <summary>
        /// Serializes and writes <paramref name="data"/> immediately, on the calling thread.
        /// </summary>
        /// <returns>
        /// <c>false</c> when nothing was written: either the backend cannot write synchronously, or
        /// another save for the same key is already in flight. This method never waits for that other
        /// save — its only caller is a Unity lifecycle message that cannot afford to block, and the
        /// in-flight write already carries data at most one debounce window old.
        /// </returns>
        /// <exception cref="System.Exception">
        /// Propagates whatever the write itself threw — a full disk, an unserializable field. Only
        /// <em>declining</em> to write returns false; <em>failing</em> to write throws, exactly as
        /// <see cref="ISaveService.SaveAsync{T}(string, T, System.Threading.CancellationToken)"/>
        /// does. A caller invoking this from its own OnApplicationPause must therefore catch, or a
        /// failed save becomes an unhandled exception inside a Unity lifecycle message.
        /// </exception>
        bool TrySaveSync<T>(string key, T data) where T : class;
    }
}
