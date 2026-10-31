# Audio UI Starter

A working UI sound setup: one click sound, played four ways (click, toggle, popup show and popup hide, each at a different pitch).

## Use it
1. Assign `AudioConfig` to your `UIFrameworkConfig.Audio`.
2. Buttons and toggles: add `UIButtonSound` (mode Default) next to each one. Popup-layer views play the show and hide cues automatically.
3. With `LoaderMode = Resources` it works as is: the resident bank key `AudioUiStarter/UiBank` is the Resources path. With Addressables, mark `Resources/AudioUiStarter/UiBank` addressable, change its address to that key, and move it out of `Resources`.

## Make it yours
- Swap `Click.wav` for your own sounds. Use `Assets > Create > UIFramework > Audio > Sound Events From Selected Clips` to create one event per clip.
- Put a `Sound Ids Codegen Settings` asset at your game root and run `Tools > UIFramework > Audio > Generate Sound Ids`. Game code can then play `SoundIds.Ui_Click`.
- In your game's EditMode tests, assert that `SoundValidator.Validate("Assets/Games/YourGame").Errors` is empty.

The click is a synthesized 60 ms tone, generated for this sample. It's free to use.
