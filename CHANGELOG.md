# Changelog

## [Unreleased]

## [1.1.0] - 2026-08-01

### Added
- Save/load persistence system: `ISaveService`/`JsonSaveService` (Newtonsoft.Json, atomic-write, per-key re-entrancy guarded) + `IStorageBackend`/`LocalFileStorageBackend` (the storage swap seam — local disk today, cloud-swappable later). `SaveAsync<T>(data)`/`LoadAsync<T>()` with key defaulting to `typeof(T).Name`; missing save returns `null`, corrupt save throws. R3 `OnSaveStarted/Completed/FailedAsObservable` events. Registered as singletons in `UIFrameworkLifetimeScope`. Adds `com.unity.nuget.newtonsoft-json` as a `package.json` dependency (already resolved transitively via Addressables in every known consumer — zero install burden).
- Key-less `ExistsAsync<T>()`/`DeleteAsync<T>()` overloads, so a consumer using the default `typeof(T).Name` key never has to hardcode the key string anywhere. Note `T` cannot be inferred: write `ExistsAsync<PlayerData>()`, not `ExistsAsync()`.
- `SaveSchemaVersionException` — thrown when a save file's schema version is newer than the running build supports.
- First test assembly for the framework (`Tests/Runtime`, PlayMode): tests covering save/load round-trip with a `Dictionary<,>` field, the known corruption modes, backup recovery, schema-version enforcement, the R3 event contract and disposal semantics. One corruption mode remains undetectable by design and is documented in `SaveEnvelopeCodec` (a structurally valid envelope holding a different type's payload). Consumers must add `"testables": ["com.sinkii09.uiframework"]` to their `manifest.json` to run them.
- Reliability hardening for the persistence system: `LocalFileStorageBackend` now sweeps orphaned `.tmp` files (left by a crash mid-write in a prior session) once on construction, and maintains a single rolling `<key>.json.bak` backup on every overwrite via `File.Replace`'s backup parameter. `JsonSaveService.LoadAsync<T>` transparently recovers from the backup (with a `Debug.LogWarning`) if the primary save exists but fails to deserialize; `DeleteAsync` removes the backup too, so deleted keys don't leak a `.bak` behind them.
- `ResourceCounter` control (`Runtime/Controls/Core/`) — icon + "current/max" label + button, joins the `Badge`/`IconLabel`/`ProgressBar` `UIControlBase` family. Button click exposed as `OnClickedAsObservable` (R3), same outward-event convention as `Draggable`/`DropZone`.
- `GameObject/UI/UIFramework/*` quick-create menu items (`Editor/Tools/MenuItems/CreateUIControlMenuItems.cs`) for `Badge`, `Icon Label`, `Progress Bar`, `Resource Counter` — pre-wired hierarchy + private field wiring, auto-creates a `Canvas`/`EventSystem` if the scene has none, matching Unity's own `GameObject > UI > Button` UX.

### Fixed
- **Silent save loss in `LoadAsync<T>`.** Newtonsoft only throws for *malformed* JSON — a present-but-wrong-shaped file (`{}`, `"null"`, empty, or a newer schema) deserialized to a null payload, which the service reported as "no save yet". The caller would start fresh and the next `SaveAsync` rotated the last good backup out, destroying the save with no log or exception. A present file that is not a valid envelope is now corruption: backup recovery is attempted, and if that fails it throws. Missing-file is the only path that still returns `null`.
- **`SchemaVersion` was written but never read**, so the envelope's forward-insurance bought nothing — the first schema change would have silently taken the path above. It is now enforced on load: newer than the running build throws `SaveSchemaVersionException` (and deliberately does *not* fall back to the backup, which is the player's own older progress); older logs a warning and loads as-is. There is still no migration engine, only the check.
- **A cancelled save left `OnSaveStarted` with no terminal event**, so a "Saving…" indicator bound to Started/Completed would hang forever. `OnSaveFailed` now also fires on cancellation, carrying the `OperationCanceledException`. On that path the event may arrive off the main thread — use `ObserveOnMainThread()` before touching UI.
- `LocalFileStorageBackend.ExistsAsync` performed synchronous main-thread file I/O and ignored its `CancellationToken`, contradicting the class's own "all file I/O runs off-thread" contract. All four operations now run on the thread pool and observe cancellation uniformly.
- `package.json` was missing a `com.unity.inputsystem` dependency entry despite `Runtime` hard-referencing `Unity.InputSystem` since 1.0.0 — added (also now required by the new `Editor.Tools` menu items).

### Changed
- **Breaking (but unreleased):** `SaveAsync` now throws `ArgumentNullException` on null data instead of writing a null payload — use `DeleteAsync` to remove a key. This is what makes a null payload on disk an unambiguous corruption signal at load time. The persistence system had no released consumers, so nothing existing can break.
- `JsonSaveService` implements `IDisposable` (VContainer disposes it on scope teardown, completing the three R3 subjects). `ISaveService` deliberately does *not* declare it. Publishing after disposal is a safe no-op, so an in-flight save at teardown completes silently rather than throwing.
- Saves serialize compactly in builds (`Formatting.None`) and stay indented in the editor — roughly half the file size and parse cost on device, unchanged readability while developing.

## [1.0.0] - 2026-05-31

### Added
- Initial release
- R3 Reactive MVVM (UIViewBase, ViewModelBase, R3 bindings)
- Navigation system (UINavigator, NavigationStack, UIStateMachine)
- Async loading and object pooling
- DOTween Pro transitions + Feel juicy UI integration
- VContainer DI integration with layered Canvas (UIRoot)
- Game lifecycle manager (AppStateMachine, SceneLoader)
- UI controls (tabs, safe area)
- Editor wizard for project scaffolding

### Tested against
- Unity 6000.4.0f1
- VContainer 1.18.0
- R3 1.3.1
- UniTask 2.5.11
- Addressables 2.6.0
- DOTween Pro (latest Asset Store)
- Feel / MoreMountains (latest)
