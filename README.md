# Mouse Clicker

A small Windows app that clicks the mouse repeatedly, or holds a key or mouse button down, with global hotkeys.

## Features

### Clicker
- Click interval from 10 to 1000 ms, set with the slider or by typing a value.
- Random jitter of up to 50% so clicks don't land on an exact rhythm.
- **Toggle** mode: press the hotkey once to start, again to stop.
- **Hold** mode: clicks only while the hotkey is held down.
- Left, right or middle button, with an optional double click.
- Click at the cursor, at a fixed screen point, or at a spot inside a window (found again by process name).
- Auto-stop after a number of clicks or minutes.
- Optional Windows notification when clicking starts.
- Click counter and last-click time.
- Default hotkey: F6 (changeable).

### Hold
- Press the trigger key (default F7) to start holding a key (default W). Press it again to release.
- Hold a keyboard key, a mouse button, or both.
- Single tap: toggle on and off, hold while the trigger is pressed (with an optional maximum time), or do nothing.
- **Timed hold**: a double tap holds for a set time (default 5 s) and a triple tap for another (default 10 s). Optionally, tapping during a timed hold adds time. The time left shows in the status pill.
- Enable or disable the trigger with **Enable hold trigger**.
- **Stop hold when I press or release the held key**: releases the held key when you press or release that key yourself (off by default).
- The held key is pressed again when the window you are using changes, so the hold is not lost when you switch windows.

### Settings
- **Profiles**: save named sets of clicker and hold settings, and switch between them from a list or with a key (default F8).
- Start with Windows, start minimized, and minimize to the tray.

The four keys (hotkey, trigger, held key, next profile) must be different.

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

Pushing to `main` runs the release workflow (`.github/workflows/release.yml`). It creates a GitHub release only when the `<Version>` in `src/MouseClicker/MouseClicker.csproj` has not been released yet, so bump the version before pushing a new release. If `release-notes/v<version>.md` exists it is used as the release notes; otherwise they are generated from the commits.

## Settings

Settings are saved to `%APPDATA%\MouseClicker\settings.json`.
