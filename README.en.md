# Apex Audio

Windows game-audio capture and supplemental-haptics tool developed for the Flydigi Apex 6 Pro. The host manages connections, audio-group presets, multitrack recording and playback, and can route selected game audio tracks to Flydigi X-Haptics. Game-specific capture is supplied by `.gpack` adapters.

[中文说明](README.md)

## Downloads

This repository currently contains source code and adapter-development documentation; a ready-to-run application archive has not been published yet. The application requires Windows x64, .NET 8 Desktop Runtime, and WebView2 Runtime. Supplemental haptics are currently designed and verified primarily with the Flydigi Apex 6 Pro.

Import game adapters from the app as `.gpack` files. Each package only matches the game executable version declared in its manifest. Game updates may require an updated package. Supplemental haptics require a compatible official X-Haptics installation.

Game adapters are best distributed as versioned GitHub Release assets so users can download only the games they need. The source tree does not include recordings, user settings, or local game data.

## Develop an adapter

Start with [START-HERE.md](START-HERE.md), then read the [game-pack authoring guide](docs/gamepack-authoring.md). Generic templates and engine adapters are included; ready-to-import game packages are distributed through GitHub Releases.

## Build from source

Windows x64, PowerShell 7, and the .NET 8 SDK are required. See the [development guide](docs/development.md) and [build environment](docs/build-environment.md).

```powershell
pwsh -File tools/Build.ps1 -Test
```

This project is licensed under the [MIT License](LICENSE). See [THIRD-PARTY.md](THIRD-PARTY.md) for separate terms.
