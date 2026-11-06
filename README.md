# ProfileOnPC

Windows desktop app for managing profiles that close and launch selected apps, open websites, and apply optional cleanup and system optimizations.

## Features

- Per-profile app launch/close lists and website shortcuts
- Light and dark themes
- Optional cleanup, power-plan, and Windows settings actions
- Tray support, global hotkeys, activation history, and process triggers
- Profile data and settings stored in `%LOCALAPPDATA%\ProfileOnPC`

Some system-level actions require administrator privileges. Review each action before enabling it; changing Windows services, power settings, or cleanup targets can affect the system.

## Requirements

- Windows 10 or Windows 11 (x64)
- .NET 8 SDK to build from source

The published app is self-contained, so the .NET runtime does not need to be installed on the target PC.

## Build

From PowerShell in this repository folder:

```powershell
.\Build.ps1
```

The output folder is `publish\`. Copy the **entire folder** to another PC and run `ProfileOnPC.exe`.

To build for Windows ARM64:

```powershell
.\Build.ps1 -Runtime win-arm64
```

Alternatively, open `ProfileOnPC.sln` in Visual Studio 2022 with the .NET desktop development workload.

## First use

1. Start the app and create a profile.
2. Add applications to start or close, websites to open, and optional optimization actions.
3. Save the profile, then activate it when needed.

Example profile data is provided in `example_profiles.json`; it is a reference file and is not imported automatically.

## Project structure

- `*.xaml` — WPF windows and application resources
- `Models/` — profile and settings models
- `Services/` — process, storage, optimization, hotkey, and Windows integration
- `Helpers/` — shared UI helpers
- `Themes/` — light/dark palettes and control styles

## License

No license has been included. Add a license before accepting contributions or granting reuse rights.
