namespace Sinkii09.UIFramework
{
    // Optional capability: land a payload without awaiting anything.
    //
    // Exists for exactly one caller — the app-pause flush. OnApplicationPause is a synchronous Unity
    // message that cannot be awaited, and the player loop stops the moment it returns, so a UniTask
    // continuation scheduled inside it may never resume. Blocking the main thread on the async path
    // instead is worse, not safer: JsonSaveService.SaveAsync awaits a per-key SemaphoreSlim whose
    // holder resumes on the player loop, so waiting there deadlocks the app on the way out.
    //
    // A separate interface rather than a member on IStorageBackend, for two reasons. Adding a member
    // breaks every implementation — that interface is documented as a swap seam — and a backend that
    // talks to a network (cloud save, Steam) genuinely cannot honour this. The honest answer for
    // those is to not implement it; callers detect that with a type test and fall back.
    //
    // IStorageBackend.WriteAsync's ATOMICITY contract applies here unchanged: a reader sees the old
    // payload or the new one, never a partial write. Durability is weaker by nature — the process
    // may be killed part-way through this call — and that is survivable precisely because the write
    // is atomic: the previous save stays intact rather than becoming a torn file.
    public interface ISynchronousStorageBackend
    {
        void WriteSync(string key, string contents);
    }
}
