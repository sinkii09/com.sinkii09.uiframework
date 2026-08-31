# Changelog

## [Unreleased]

## [1.8.0] - 2026-09-01

Async lifetime utilities. Three additive pieces sharing one theme: **async work started from UI
must have a defined owner, a defined cancellation point, and a defined restore-on-exit.** No
existing signature changed; no migration needed.

### Added
- **`ViewModelBase.ShowToken`** — the token half of `_showDisposables`. Cancelled when the view
  hides, replaced with a fresh one on the next show. Pass it to any async work started from
  `OnShow()`. Until now a ViewModel had a per-show *disposable bag* but no per-show *cancellation*,
  so a countdown or a fetch begun in `OnShow()` ran until the ViewModel itself was disposed — which
  for a cached view may be never. Views were already covered (`OnShowAsync`/`OnHideAsync` receive a
  `ct`); this closes the ViewModel side.
- **`UIBindingExtensions.BindButtonAsync`** — button click → `Func<CancellationToken, UniTask>`,
  with a re-entrancy guard and the same automatic listener removal as `BindButton`. The house style
  was a synchronous `UnityAction` firing `.Forget()`, and nothing stopped the second and third press
  from launching that many concurrent operations. `UINavigator`'s `_isTransitioning` guard does not
  help — it protects navigation only, and only by silently dropping the call.
  `disableWhileRunning` defaults to **false**: the guard is what makes this correct, greying the
  button out is cosmetic, and it is the only mode that conflicts with `BindToInteractable` on the
  same button.

### Notes
Three traps this is built against, none of which produce a compiler diagnostic:

- **`CancellationTokenSource.Token` throws `ObjectDisposedException` after `Dispose()`.** `ShowToken`
  returns `new CancellationToken(true)` once the ViewModel is disposed rather than touching the
  dead source — work started against a dead ViewModel should stop, not run against `None`.
- **`Cancel()` runs its registrations synchronously and rethrows them wrapped.** An exception from
  someone else's continuation must not abort `NotifyHide` and skip the teardown, or every per-show
  subscription leaks. Both the cancel *and* the bag disposal are contained: the CTS swap lives in a
  `finally`, because a throw on the way through would otherwise strand the ViewModel on a cancelled
  token **forever** — every later show silently no-opping all gated work. That is reachable, not
  theoretical: `Button.onClick.RemoveListener` throws `MissingReferenceException` once the Button is
  destroyed, and `BindButtonAsync` registers exactly that disposable.
- **The swapped-out source must be disposed, but only after the field is swapped.** Disposing it
  earlier makes `ShowToken` throw for anything `OnHide()` calls; not disposing it at all keeps its
  registrations — and any linked source built from it — rooted for the ViewModel's whole life.

`TabIndicator`'s bare `DOKill()` gained a comment, not a change: it is correct today because it
tweens `anchoredPosition` only and every `MoveTo` writes a fresh target, and it silently becomes a
corruption site the moment anyone adds a scale or colour tween there.

### Tested against
- Unity 6000.4.0f1 — EditMode 267 passed, PlayMode 205 passed

## [1.7.0] - 2026-08-31

Tooltips: a resident, single-instance tooltip owned by the framework, driven by a small trigger
component. Four input sources, two content models. Opt-in — a project with no tooltip view in its
scene gets `NullTooltipService` and behaves exactly as before.

**Existing projects must run `Tools/UIFramework/Upgrade UIRoot Layers` once.** See Migration below.

### Added
- **`ITooltipService` / `TooltipService`** — owns the one tooltip: which view is up, what it is
  anchored to, and the timing state machine `Idle → Pending → Shown → Grace → Idle` with an instant
  re-show window (sweeping across a grid shows immediately rather than re-waiting the dwell). It is
  deliberately **not** a navigation view: `UINavigator`'s `_isTransitioning` guard silently drops
  concurrent calls, and back-button and `MaxNavigationDepth` semantics are all wrong for something
  that fires ten times a second. It follows `TransitionOverlayView`'s residency model instead —
  extends `UIViewBase`, not `UIView<T>`, so `UIViewRegistry.AutoRegister` never sees it.
  The machine advances in `Tick()` off `Time.unscaledDeltaTime`, not by awaiting `UniTask.Delay`, so
  tooltips still appear while the game is paused at `timeScale = 0`.
- **`TooltipViewBase` / `TooltipView` / `TooltipContent`** — the built-in composable-sections
  tooltip (title, icon, body, variable-length stat lines, footer), every section optional. Subclass
  `TooltipViewBase` and set its `_viewKey` for a project-specific look; a payload's `ViewKey` picks
  the view, and null or empty routes to the built-in one.
- **`TooltipTrigger`** (`Runtime/Controls/Core/`) — mouse hover, mouse click, gamepad/keyboard
  focus, and touch long-press. Payload comes from an `ITooltipSource` on the widget, queried at show
  time so a value changed since bind is current; failing that, from serialized inline title/body, so
  a static hint needs no extra component. `NotifyContentChanged()` releases the tooltip when a
  pooled cell is rebound in place.
- **`Tools/UIFramework/Upgrade UIRoot Layers`** — adds missing layer children to an existing UIRoot
  and wires the `_layers` references. Idempotent; skips prefab variants; also scans open scenes.
  This also fixes a long-standing gap: the installer wizard created the layer GameObjects but
  **never assigned `_layers` at all**, for any of the layers.
- Five `UIFrameworkConfig` fields: `TooltipShowDelaySeconds`, `TooltipHideGraceSeconds`,
  `TooltipReShowWindowSeconds`, `TooltipLongPressSeconds`, `TooltipLongPressMoveCancelPixels`.

### Changed
- **`UILayer` gained a `Tooltip` member, inserted between `Popup` and `Overlay`** (sortOrder 250) —
  above popups so tooltips work inside modal dialogs, below `Overlay` so they can never draw over a
  loading curtain. The ordinal is load-bearing (`UIRootLayerRefs.BlockLayersBelow` compares
  `(int)layer`), so this renumbers `Overlay` and `Debug`. It is safe only because no `UILayer` value
  is serialized anywhere — every use is a code-level `override Layer => UILayer.X`. **Verify that
  again before inserting another member.**
- `UINavigator` takes a trailing-optional `ITooltipService` and calls `HideImmediate()` in
  `ShowAsync`, `ShowAsync<T,TArgs>`, `CloseAllAsync` and `ChangeStateAsync`. Layer blocking only
  toggles raycasters and never touches visibility, so without this a tooltip outlives the screen
  that raised it. Trailing-optional purely for **source** compatibility, exactly as `UIBackdrop` is:
  VContainer ignores C# default parameter values, so a hand-built container must still register one.
- `Editor.Tools` asmdef now references `Unity.ugui`.

### Migration
`UIRootLayerRefs` serialises by field *name*, so an existing UIRoot deserialises `Tooltip` as
**null** — and `SetLayerInteractable` returns *silently* on a null transform, making the failure
invisible. Run `Tools/UIFramework/Upgrade UIRoot Layers` once per project. If the layer is still
missing at runtime the service falls back to the `Overlay` layer and logs one error naming the
command, so tooltips degrade rather than vanish.

### Notes
Four traps this implementation is built against, each of which compiled clean:
`UIViewBase.ShowAsync` re-enables `blocksRaycasts` after every show (a tooltip that takes raycasts
strobes under the cursor, so it is re-asserted after both show and hide);
`LayoutRebuilder.ForceRebuildLayoutImmediate` is a no-op on an inactive GameObject (bind and measure
before activating and every appearance is sized against the *previous* payload);
`UIViewBase.HideAsync` deactivates the GameObject in both its normal and its cancellation path, so
view animations are **serialized rather than cancelled**; and a show that bails out must tear the
current tooltip down rather than merely reset state, or it strands one on screen with the anchor
watchdog switched off.

### Tested against
- Unity 6000.4.0f1 — EditMode 257 passed, PlayMode 198 passed


## [1.6.0] - 2026-08-29

Per-view policy, and the three production concerns a long session forces you to solve: cache
eviction, modal backdrops, preloading. **Everything defaults OFF** — a project that upgrades and
changes nothing behaves identically, and no public interface changed.

### Added
- **`UIViewPolicyConfig`** (`Create > UIFramework > View Policy Config`) — per-view `Resident` /
  `NeedsBackdrop` / `PreloadOnBoot` flags, plus `UIViewPolicyResolver` to resolve them. Assign it on
  `UIFrameworkLifetimeScope`; leaving it empty means framework defaults for every view. Entries are
  keyed by the view's **load key** (its `[UIViewKey]` value, else the class name) because a
  ScriptableObject cannot serialize a `Type` — declaring policy under the class name of a view that
  carries `[UIViewKey]` therefore does nothing, so boot-time validation warns on any key matching no
  registered view.
- **Timed cache eviction** — `UIViewCacheSweeper` periodically destroys views idle past
  `UIFrameworkConfig.ViewCacheGraceSeconds` (`0` = disabled, the default). Previously
  `UIViewFactory._cache` only ever grew: every view a player opened held its GameObject and its
  loader handle for the whole session. A view is evicted only when it is not pending, not on the
  navigation stack, not `Resident`, not visible, not active, and idle past the grace period.
- **`UIBackdrop`** — one reusable dimming `Image` parked directly beneath any view whose policy sets
  `NeedsBackdrop`, so popup prefabs stop hand-rolling their own. Colour from
  `UIFrameworkConfig.BackdropColor`. Driven by `UINavigator.RefreshLayerBlocking`, i.e. the same
  authority as layer blocking.
- **`UIViewPreloader`** — warms views marked `PreloadOnBoot` so their first `ShowAsync` doesn't pay
  the asset load, the `Instantiate` or the layer reparent. Nothing preloads automatically; call
  `PreloadAllAsync()` from your own boot sequence. `PreloadOnBoot` implies `Resident`, or the sweeper
  would destroy exactly what preload warmed. Note it does **not** save the child scope or the
  ViewModel — those are rebuilt on first show, so keep ViewModel constructors free of side effects.
- **`UIViewKeys.For(Type)`** — single source of the load-key derivation. `UIViewFactory.GetKey` and
  `UIViewRegistry.AutoRegister` previously implemented the same rule independently, with nothing
  keeping them in agreement.

### Fixed
- **`UINavigator.ShowAsync` could leave layer blocking applied for a view that was never pushed.**
  `NavigationStack.PushAsync` *warns and returns* at `MaxNavigationDepth` rather than throwing, so
  the pre-push refresh was left describing a view nobody could see or close. It now re-derives from
  the real top of stack whenever the push did not land, and its `catch` refreshes before rethrowing.
  Previously an invisible raycaster mis-toggle; with a backdrop it would have been a full-screen
  softlock.
- **A view deactivated by a failed hide no longer raises a backdrop.** `UIViewBase.HideAsync`
  deactivates then rethrows, and `NavigationStack.PopAsync` propagates before removing, so a view
  whose hide animation threw is briefly deactivated *and* still top of stack.

### Changed
- `builder.Register<IUIViewFactory, UIViewFactory>` now carries `.AsSelf()`, so the concrete type
  resolves to the same singleton for the sweeper and preloader.

## [1.5.0] - 2026-08-23

RecyclerView Phase 2 — variable cell size.

### Added
- **`RecyclerView.SetItemSizeProvider(Func<int, float>)`** — per-index cell sizes along the scroll
  axis. Sizes are **declared, never measured**: the view asks before it binds, so the content extent
  and every cell position are exact from the first frame and the list never shifts under the user to
  correct an estimate. The trade is that a cell may not size itself; the editor-only measurement
  check reports one that tries. Pass `null` to return to the uniform `RecyclerViewSettings.CellSize`.
- **`RecyclerView.RefreshSizes()`** — re-asks the provider for every index and re-lays out, keeping
  the scroll position. Call it after changing whatever state the provider reads (an expanded row, a
  re-measured message). There is deliberately **no per-index size setter**: the provider is the single
  source of truth, so an override stored beside it would be silently discarded by the next
  `SetItemCount`, and one keyed by index would outlive the item it was meant for as soon as the
  list's contents shifted. Keep the size in your own data and re-ask.
- Internal `IItemOffsets` with `UniformOffsets` and `PrefixSumOffsets` implementations. The window's
  decision core needed no change — `RecycleWindow.Decide`/`NeedsReseed` already compared offsets
  rather than stride — so this replaces the offset *supply*, not the recycling logic.

### Fixed
- **A pooled cell kept the size of whichever index first instantiated it.** `ContentLayout.ConfigureCell`
  ran once per `Instantiate`, which was correct while every cell was the same size. Sizing is now
  applied per bind, in both bind paths.
- **`Rebind` never wrote a size at all**, so `RefreshIndex` on a multi-prefab list rendered the
  refreshed cell at whatever size its replacement carried out of the pool. Invisible to any
  single-prefab test.
- **The content rect was only ever rebuilt by `SetItemCount`.** Under uniform sizing count was the
  only thing that could move an offset; it no longer is, so both new mutators rebuild it along with
  every live cell's cached offset.
- `RecycleWindow.MaxIterationsFor` now takes the list's **smallest stride** rather than a single
  uniform one. A list of 200px rows carrying one 4px separator converges at the rate of the 4px row,
  and averaging would reproduce the fixed-cap defect this replaced: a bound that is usually generous
  and occasionally, silently, short.
- Reentrancy: a size provider that mutates the list is now refused. The provider runs outside the
  pump, so the pump's own guard did not cover it.
- `CellHandle.MeasuredSize` renamed `DeclaredSize` — it has never held a measurement.

## [1.4.1] - 2026-08-18

### Fixed
- **Removed three orphaned `.meta` files whose folders no longer exist**, which made every consuming
  project log a warning on import: `Runtime/Core/Pooling.meta` (left behind when the pool system was
  removed), `Runtime/Resources.meta` and `Runtime/Resources/UIFramework.meta` (empty folders the
  package never shipped anything into). Git cannot store an empty directory, so on a fresh clone
  Unity found each `.meta` without its folder, recreated the folder, and warned — once per consumer,
  every import. Nothing in `Runtime/` or `Editor/` referenced either path.

  No API or behaviour change; `Resources.Load` searches every `Resources` folder in the project, so
  an empty one in the package contributed nothing.

## [1.4.0] - 2026-08-18

RecyclerView Phase 1. The suite written alongside it had never been executed; running it surfaced 18
failures (all test-side) and a review found one CRITICAL in the runtime, fixed below. Verified at
EditMode 62/62 and PlayMode 90/90, with 19 new integration tests covering the ~530 LOC that
previously had none. Plan: `plans/260818-2139-recycler-view-phase1-completion/` in the consuming
project.

### Added
- **`RecyclerView`** (`Runtime/Controls/Collections/`) — a recycling list whose live cell count
  tracks the viewport rather than the data: 10,000 items realise ~11 cells. Written to replace
  SuperScrollView rather than wrap it. Phase 1 assumes a uniform, declared cell size; variable
  sizing is phase 2. Ships with a two-tier pool (a cell recycled off the head is reusable at the
  tail in the same frame with no `SetActive` round-trip and no `Instantiate`), reseed-on-jump, and
  four scroll directions collapsed into one code path.
  - `RecycleWindow` is a pure static decision function — no Unity types, no state — so termination
    is proved by EditMode tests rather than bounded by a bail-out counter.
  - Provider contract is enforced, not trusted: the cell must come from `RentCell`, exactly one per
    call, and never `null`. Each violation silently corrupts the window otherwise.
- Quick-create menu item `GameObject > UI > UIFramework > Recycler View`.
- Sample `Samples~/RecyclerViewList` — a 50,000-row leaderboard consumer.

### Fixed
- **`RecycleWindow`'s iteration cap was a fixed 64 that ordinary configurations exceeded.** A reseed
  grows the window one cell per iteration, so the work scales with how many cells fit in the
  viewport plus a create band at each end — a 1920px-tall viewport with 30px rows needs ~77. Past
  the cap the pump logged an error and abandoned the tick, leaving a permanently under-filled list
  and a per-frame error flood. Replaced with `RecycleWindow.MaxIterationsFor`, derived from the
  geometry at pump time.
- `SetCellProvider` now pumps, so installing the provider after `SetItemCount` no longer leaves the
  list blank until the next `Update()`.
- `RentCell` now refuses the two contract violations that used to leak a cell silently: being called
  outside the provider, and being called twice for one index (only the returned cell is tracked).
- A provider that rented a cell and then returned `null` leaked that cell permanently; it is now
  returned to the pool before the contract violation throws.
- `RefreshIndex` on a provider that throws no longer strands a staged cell that the shown-window
  still references — the slot is dropped and the next pump refills it.
- `ScrollToIndex` and `ForEachShownCell` now carry the same reentrancy guard as the other public
  mutators, so a call from inside a cell provider is refused loudly instead of corrupting the window.

### Docs
- `ScrollAxis.ViewportStart` was documented as the inverse of `ToLocal`. It is the **negative**:
  `ToLocal` places cells inside the content root, `ViewportStart` reads the content root's own
  position, and the content travels the opposite way to reveal later items. The wrong doc had
  already produced 8 wrong tests.


## [1.3.0] - 2026-08-02

Animation/transition subsystem audit — 1 CRITICAL + 2 WARNING findings, each closed with dedicated
regression tests, verified by reverting each fix in isolation and confirming its test goes red
before restoring it (one revert surfaced a genuine additional gap in the initial fix — see the
`interactable`/`blocksRaycasts` note below — caught before this release, not after). Plan:
`plans/260802-1358-animation-transition-hardening/plan.md` in this repo.

### BREAKING
- **`ILoadingContext.OnLoaded` removed; `Set()` no longer takes an `onLoaded` callback.** The old
  pattern — `_loadingContext.Set(scene, ct => _lifecycle.ChangeStateAsync<GameplayState>(ct))`,
  invoked by `LoadingState.OnEnterAsync` after the scene loaded — deadlocked: the callback ran
  nested inside `GameLifecycleManager.ChangeStateAsync<LoadingState>`'s still-`true`
  `_isTransitioning`, so the nested call silently no-op'd against GLM's own reentrancy guard. The
  state machine ended up permanently stuck on `LoadingState`, even though the outer call reported
  success. No known consumer had wired this pattern up yet. **Migration:** replace
  `_loadingContext.Set(scene, ct => _lifecycle.ChangeStateAsync<TNext>(ct))` +
  `_lifecycle.ChangeStateAsync<LoadingState>(ct)` with a single
  `_lifecycle.LoadSceneAndChangeStateAsync<TNext>(scene, ct)` call.

### Added
- `GameLifecycleManager.LoadSceneAndChangeStateAsync<TNext>(sceneName, ct)` — loads a scene via
  `LoadingState` then transitions directly into `TNext`, both steps composed as sequential sibling
  calls (not nested) under one overlay/guard window, avoiding the deadlock above by construction.

### Fixed
- **A view could be clicked before its own `OnShowAsync` setup finished.** `DOTweenUIAnimator` used
  to restore `CanvasGroup.interactable`/`blocksRaycasts = true` itself — immediately on tween
  completion, or synchronously (before `OnShowAsync` even started) when no `_showTransition` was
  assigned. `UIViewBase.ShowAsync` is now the sole owner of restoring them to `true`, and only after
  `OnShowAsync` completes. (The null-transition branch needed an explicit `interactable = false`
  added too — without it, a freshly-added `CanvasGroup` defaults to `interactable = true` via
  Unity's own component default, so the view would have stayed clickable for the view's entire
  `OnShowAsync`, not just briefly; caught by the regression test itself during implementation.)
- **`CanvasGroup.alpha` could get stuck at 0 when mixing transition types.** `FadeTransition`/
  `ZoomOutFadeTransition` drive alpha themselves and happened to land correctly, but
  `ScaleTransition`/`SlideTransition` never touched alpha at all — assigning e.g. a Fade hide with a
  Scale show on the same view left alpha stuck at 0 after the first hide, forever (the view scales
  back into place on the next show but stays invisible). `DOTweenUIAnimator` now unconditionally
  normalizes `alpha` to 1 (show) / 0 (hide) after any successful transition, regardless of type.

## [1.2.1] - 2026-08-02

Phase 2 of the 2026-08-01 consolidated audit — the "crash / data-loss prevention" cluster (C4, C5,
and a `UIViewRegistry` reflection-swallow finding), deferred from the v1.2.0 correctness cluster.
All three are backwards-compatible; no public API removed. Full findings:
`plans/reports/code-review-260801-2110-uiframework-consolidated.md` in the consuming project;
implementation plan: `plans/260802-1122-hardening-cluster/plan.md` in this repo.

### Fixed
- **`ViewViewModelCreatorWizard` no longer silently overwrites existing files.** Re-running the
  View/ViewModel generator with a name colliding with an existing (possibly hand-edited) file now
  shows a confirmation dialog naming exactly which file(s) would be overwritten; Cancel aborts
  writing either file, so the pair never ends up half-regenerated.
- **`ISafeAreaProvider` has a Null-Object fallback.** A scene missing a `SafeAreaProvider`
  component used to crash VContainer resolution instead of degrading gracefully. It now falls back
  to a full-screen `NullSafeAreaProvider` (mirrors the existing `ITransitionOverlay`/
  `NullTransitionOverlay` pattern), with a `Debug.LogWarning` instead of a hard error.
- **`UIViewRegistry.AutoRegister` no longer silently drops an entire assembly's views on a partial
  reflection failure.** A bare `catch { continue; }` around `Assembly.GetTypes()` treated any
  `ReflectionTypeLoadException` (common under IL2CPP stripping) as "skip this whole assembly,"
  discarding types that loaded fine along with the ones that didn't, with zero diagnostic. Now
  catches `ReflectionTypeLoadException` specifically, recovers the loadable types via
  `ex.Types.Where(t => t != null)`, and logs a warning naming the assembly and failure count.
- **`Editor.Tools` assembly now actually compiles.** Unrelated latent bug found while implementing
  the above: `com.sinkii09.uiframework.editor.tools.asmdef` had `defineConstraints` for
  `SINKII09_UNITASK`/`SINKII09_R3`/`SINKII09_VCONTAINER` but no matching `versionDefines` block to
  define them — permanently unsatisfiable constraints, so Unity silently excluded the whole
  assembly (both setup wizards, the View/ViewModel generator, the custom inspector, all menu
  items) from compilation in every consuming project, with no error anywhere. Fixed by adding the
  same `versionDefines` block already used by the runtime and test assemblies.

### Added
- `Tests/Editor/` — new Editor-only test assembly (`Sinkii09.UIFramework.Tests.Editor`) for
  `ViewViewModelCreatorWizard` coverage, since it depends on the Editor-only `Editor.Tools`
  assembly and the main `Tests/Runtime` assembly isn't platform-restricted.

## [1.2.0] - 2026-08-01

A 4-way parallel adversarial review of the whole framework (101 files) re-verified every CRITICAL
finding the 2026-06-20 review claimed fixed, plus a fresh pass over code added since. 4 of 9 old
fixes turned out to be only partial (the fix landed on one of two parallel code paths, not both);
5 new CRITICALs were found. This release closes the "Phase 1 — correctness" cluster: 5 findings
(C1/C2/C3/P1/P2), each closed with a dedicated regression test, verified by reverting each fix in
isolation and confirming its test goes red before restoring it. Full findings:
`plans/reports/code-review-260801-2110-uiframework-consolidated.md` in the consuming project.

### BREAKING
- **`IUINavigator.ChangeStateAsync<TState>` removed from the public interface.** It now lives as an
  `internal` method on the concrete `UINavigator` class, reachable only by `GameLifecycleManager`
  (which now depends on the concrete `UINavigator` type, not the interface, specifically to reach
  it). State transitions go through `GameLifecycleManager.ChangeStateAsync<T>()` for a different
  state, or `RestartCurrentStateAsync()` for same-state re-entry (e.g. Retry). `IUINavigator.ResetState()`
  is unchanged and still public. **Migration:** any direct `_navigator.ChangeStateAsync<T>()` call
  in consuming code must become `_lifecycle.ChangeStateAsync<T>()` (different state) or
  `_lifecycle.RestartCurrentStateAsync()` (same state — a plain `ChangeStateAsync` call on the
  current state now hits the state machine's same-state guard and silently no-ops).
- **`GameLifecycleManager`-driven and `UINavigator`-driven state transitions now run the previous
  state's `OnExitAsync`.** Previously `UINavigator.ChangeStateAsync` called
  `IUIStateMachine.ResetState()` before every transition, which nulled the current-state pointer and
  made the state machine skip `OnExitAsync` entirely — silently dropping any non-view cleanup a
  state performed there (`Time.timeScale` restore, subscription disposal, spawned-object teardown).
  If a custom `IGameState`/`IViewState` has cleanup in `OnExitAsync` that appeared to never run
  before, it will start running now — audit before upgrading.
- **A cancelled `IUIView.ShowAsync` now throws `OperationCanceledException` instead of returning
  normally.** `NavigationStack` no longer pushes a never-shown view onto the stack as a hidden
  phantom entry. If any code relied on a cancelled `ShowAsync` silently "succeeding" (including via
  `.Forget()`), it will now see the exception propagate — wrap with
  `try/catch (OperationCanceledException)` at the call site if needed. `HideAsync` is deliberately
  **not** changed — it still swallows cancellation, since `NavigationStack.PopAsync` already treats
  a cancelled hide as "hidden, remove from stack," and rethrowing there would strand an invisible
  view instead.
- **The type-erased `UIViewFactory.CreateAsync` overload now binds the created view in DI under its
  concrete type**, matching the generic overload (was `UIViewBase` on this path only). A ViewModel
  injecting `UIViewBase` directly (uncommon) must switch to the concrete view type.

### Fixed
- **Duplicate view instantiation on the default auto-registration path.** `UIViewFactory` carried
  two parallel ~90-line creation implementations; the 2026-06-20 in-flight dedup fix (`_pending`)
  only covered the manual `Register<>()` path. `UINavigator` calls the *other* (type-erased) path
  for every auto-registered view, so the dedup guard was dead on the framework's default navigation
  path for over a month. Both overloads (plus the `TArgs` overload) now delegate to one
  `CreateCoreAsync`, closing the race for every caller. Also closes the Addressables
  handle-release race for `AddressablesUILoader`, whose only in-framework caller is this factory.
- **`GameLifecycleManager` bypassed `UINavigator` entirely**, calling `IUIStateMachine.ChangeStateAsync`
  directly — making the navigator's nav-stack-clearing dead code for every GLM-driven transition,
  and leaving `UINavigator._isTransitioning` unable to protect against a concurrent direct-navigator
  call landing mid-transition. Confirmed causing real inconsistency in the consuming project: one
  feature routed through `GameLifecycleManager` correctly, another called the navigator directly —
  two features on incompatible navigation paths. Fixed by routing every GLM transition through the
  (now internal) `UINavigator.ChangeStateAsync`.
- **Transition cancel-restore never actually ran, for any built-in transition, ever.**
  `TweenExtensions.AwaitAsync` calls `tween.OnComplete(...).OnKill(...)` on every awaited tween —
  DOTween's setters *replace*, not chain, so the `.OnKill(...)` restore callback each transition
  installed at tween-creation time was silently overwritten the moment the tween was awaited (always).
  A cancelled Show/Hide could leave `transform.localScale`/`anchoredPosition` at an arbitrary
  mid-tween value. Fixed by moving restore-on-cancel out of any tween callback entirely, into
  `DOTweenUIAnimator`'s own `catch (OperationCanceledException)` blocks (which already restore
  `CanvasGroup.alpha` for the same failure mode) via a new `UITransition.RestoreOnCancel(view)`
  virtual hook.
- **`UIStateMachine`'s cancellation-path rollback still double-exited a state.** The 2026-06-20 fix
  covered the general-exception branch (`_currentState = exitCompleted ? null : previous`) but the
  `OperationCanceledException` branch still unconditionally rolled back to `previous` — reintroducing
  a double-`OnExitAsync` on the next transition specifically when cancellation landed after
  `OnExitAsync` had already completed. Both branches now share the same rule.

### Added
- `UITransition.RestoreOnCancel(UIViewBase view)` — virtual hook for transform restore-on-cancel.
  Must stay stateless (transitions are shared `ScriptableObject` assets across views).

### Changed
- 20 new regression tests (`Tests/Runtime/`), one per finding above plus supporting cases —
  `UIViewFactoryConcurrencyTests`, `UINavigatorStateRoutingTests`, `TransitionCancelRestoreTests`,
  `ViewCancellationTests`, `UIStateMachineTests`. Each fix was individually reverted and its test
  confirmed to fail before the fix was restored — no vacuous tests in this set.

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
