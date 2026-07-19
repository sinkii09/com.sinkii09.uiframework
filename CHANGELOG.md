# Changelog

## [Unreleased]

### Added
- `ResourceCounter` control (`Runtime/Controls/Core/`) — icon + "current/max" label + button, joins the `Badge`/`IconLabel`/`ProgressBar` `UIControlBase` family. Button click exposed as `OnClickedAsObservable` (R3), same outward-event convention as `Draggable`/`DropZone`.
- `GameObject/UI/UIFramework/*` quick-create menu items (`Editor/Tools/MenuItems/CreateUIControlMenuItems.cs`) for `Badge`, `Icon Label`, `Progress Bar`, `Resource Counter` — pre-wired hierarchy + private field wiring, auto-creates a `Canvas`/`EventSystem` if the scene has none, matching Unity's own `GameObject > UI > Button` UX.

### Fixed
- `package.json` was missing a `com.unity.inputsystem` dependency entry despite `Runtime` hard-referencing `Unity.InputSystem` since 1.0.0 — added (also now required by the new `Editor.Tools` menu items).

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
