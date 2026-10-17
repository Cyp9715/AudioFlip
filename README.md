# AudioFlip

AudioFlip is a fork of the original [josetr/AudioDeviceSwitcher](https://github.com/josetr/AudioDeviceSwitcher).

The original `Audio Device Switcher` by `Jose Torres` is a great project and works very well for most users. This fork exists with full respect for that work. I created it to address a few bugs and environment-specific issues I encountered during daily use, while keeping the original workflow and idea intact.

<p align="center">
  <img src="src/AudioFlip%20(Package)/Images/AudioFlipSource.png" alt="AudioFlip" width="300" height="300" />
</p>

## What this program does

AudioFlip is a Windows utility for quickly switching the system default audio devices for both playback and recording.

- Hotkey-based switching: assign a shortcut and cycle through selected output devices (speakers, headset, interface).
- Microphone routing support: manage recording devices with their own commands and hotkeys.
- Communication device support: switch the Windows Default Communication Device together with your playback/recording profile.
- Background/tray workflow: keep it running in the tray and switch devices instantly without reopening settings.
- Startup support: run automatically with Windows for always-available switching.
- Command integration: generate commands that can be used in external macro/key-mapping tools.

This is especially useful when moving between gaming, calls, streaming, and work setups where audio output/input devices need to change frequently, and it is particularly helpful for dynamically switching microphone and headset setups in Discord.

## Platform and development

AudioFlip uses **WinUI 3**, Windows App SDK 1.7, and .NET 10. The supported OS baseline remains **Windows 11 24H2 (build 26100)**. The app and its MSIX packaging project are separate projects in `AudioFlip.slnx`.

Install Visual Studio 2026 with the WinUI/Windows application development and MSIX packaging tools, the Windows 11 SDK (26100), and the .NET 10 SDK. Use Visual Studio's MSBuild for the app and packaging projects; `dotnet build` alone does not provide the required Visual Studio packaging tasks.

From PowerShell in the repository root:

```powershell
./build.ps1 test
./build.ps1 build -platform x64
./build.ps1 build -platform arm64
```

Builds produce **unsigned** bundles in `AppPackages/`. Sign a bundle with your own trusted certificate before installing it, or submit the unsigned bundle through the Store publishing process. CI runs the regression tests and builds both architectures without installing audio drivers, accessing signing secrets, or publishing to the Store.

The tests cover endpoint ID changes, disconnected device selections, independent playback/recording command names, shared hotkeys, command-line quoting, switching order, notifications, and native COM signatures. They use simulated audio devices and do not change the computer's audio configuration.

Settings are stored in `settings.json` in the package's local app data folder. The previous complete save is retained as `settings.json.bak`. Existing settings from earlier AudioFlip releases are imported automatically, and the original local-settings value is retained. This avoids the 8 KB limit on a single Windows setting value.

Before distributing a release, also check the app with real devices: unplug/reconnect selected devices, switch playback and recording using a shared hotkey, close to the tray and restore by double-click, dismiss the tray menu by clicking outside it, exit from the tray, and verify startup visibility with both startup settings. These checks require an installed, signed package and interactive Windows session.
