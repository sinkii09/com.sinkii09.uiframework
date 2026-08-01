using System.Threading;
using Cysharp.Threading.Tasks;
using R3;

namespace Sinkii09.UIFramework
{
    // Save/load any plain class (POCO) with near-zero boilerplate. Key defaults to typeof(T).Name;
    // use the explicit-key overloads when two categories could share a type name, or to control
    // the on-disk filename directly.
    //
    // Constraints (documented, not code-enforced):
    // - T must be a plain C# class with no UnityEngine.Object references (Sprite, GameObject, etc.) —
    //   Newtonsoft cannot serialize engine objects sensibly.
    // - Polymorphic fields are NOT preserved: a List<ItemBase> holding derived types round-trips as
    //   the declared base and silently loses derived members. TypeNameHandling is deliberately off
    //   (it is a known deserialization RCE vector); use a discriminator field or a custom converter.
    // - Explicit keys must match ^[A-Za-z0-9_-]+$ (they become filenames); invalid keys throw
    //   ArgumentException. Default typeof(T).Name keys are valid for ordinary non-generic POCOs
    //   (the documented usage model) — a generic T's Name contains a backtick (e.g. "Wrapper`1")
    //   and fails validation; pass an explicit key for generic save types.
    // - The default key is the CLASS NAME, which is effectively a serialized filename: renaming the
    //   POCO orphans every existing save, with no diagnostic. Prefer an explicit const string key
    //   for anything that ships.
    public interface ISaveService
    {
        // Throws ArgumentNullException on null data — saving null is a programmer error (use
        // DeleteAsync to remove a key), and allowing it would make a null payload on disk
        // indistinguishable from a corrupt file at load time.
        UniTask SaveAsync<T>(T data, CancellationToken ct = default) where T : class;
        UniTask SaveAsync<T>(string key, T data, CancellationToken ct = default) where T : class;

        // Missing file returns null — that is the ONLY null-returning path. A file that is present
        // but is not a valid envelope (malformed, empty, "{}", "null", or a newer schema version)
        // is a real error: the backup is tried first, and if that fails too it throws. It never
        // silently reports "no save yet", which would let the caller start fresh and overwrite.
        //
        // Not detected: a structurally valid envelope whose payload is a different type — it loads
        // as an all-default T. Accepted limitation; see SaveEnvelopeCodec.
        UniTask<T> LoadAsync<T>(CancellationToken ct = default) where T : class;
        UniTask<T> LoadAsync<T>(string key, CancellationToken ct = default) where T : class;

        // The generic overloads resolve the key the same way SaveAsync/LoadAsync do, so a consumer
        // that never types a key never has to hardcode one. Note T cannot be inferred here: write
        // ExistsAsync<PlayerData>() or ExistsAsync<PlayerData>(ct) — ExistsAsync(ct) will not compile.
        UniTask<bool> ExistsAsync<T>(CancellationToken ct = default) where T : class;
        UniTask<bool> ExistsAsync(string key, CancellationToken ct = default);
        UniTask<bool> DeleteAsync<T>(CancellationToken ct = default) where T : class;
        UniTask<bool> DeleteAsync(string key, CancellationToken ct = default);

        // OnSaveFailed also fires when a save is CANCELLED (Error carries the
        // OperationCanceledException), so every OnSaveStarted has a terminal partner and a
        // "Saving..." indicator can never hang. On that cancellation path the event may be
        // delivered off the main thread — use ObserveOnMainThread() before touching UI.
        Observable<SaveEventArgs> OnSaveStartedAsObservable { get; }
        Observable<SaveEventArgs> OnSaveCompletedAsObservable { get; }
        Observable<SaveEventArgs> OnSaveFailedAsObservable { get; }
    }
}
