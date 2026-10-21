# Mouse Clicker

A small Windows app that clicks the left mouse button repeatedly, or holds a keyboard key down, with a global hotkey.

## Features

### Clicker
- Click interval from 10 to 1000 ms, set with the slider or by typing a value.
- Random jitter of up to 50% so clicks don't land on an exact rhythm.
- **Toggle** mode: press the hotkey once to start, again to stop.
- **Hold** mode: clicks only while the hotkey is held down.
- Optional Windows notification when clicking starts.
- Click counter and last-click time.
- Default hotkey: F6 (changeable).

### Hold
- Press the trigger key (default F7) to start holding a key (default W). Press it again to release.
- Enable or disable the trigger with **Enable hold trigger**.
- **Stop hold when I press or release the held key**: releases the held key when you press or release that key yourself (off by default).
- The held key is pressed again when the window you are using changes, so the hold is not lost when you switch windows.

The three keys (hotkey, trigger, held key) must be different.

### Status
- The Start/Stop button turns green while clicking is running.
- The activity pill beside the tabs shows **Mouse clicker active**, **Hold active**, both, or **Idle**.
- The taskbar and window icon turn green while something is active.
- The window title shows the version.

## Install

Download the latest `MouseClicker-Setup-<version>.exe` from the [Releases](https://github.com/Maciejul/MouseClicker/releases) page and run it. It installs for the current user only. A portable zip is also attached to each release.

## Build from source

Requires the .NET 8 SDK (Windows).

```powershell
dotnet build src/MouseClicker/MouseClicker.csproj -c Release
```

To create a single-file exe:

```powershell
dotnet publish src/MouseClicker/MouseClicker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o publish/portable
```

## Releases

Pushing to `main` runs the release workflow (`.github/workflows/release.yml`). It creates a GitHub release only when the `<Version>` in `src/MouseClicker/MouseClicker.csproj` has not been released yet, so bump the version before pushing a new release.

## Settings

Settings are saved to `%APPDATA%\MouseClicker\settings.json`.
