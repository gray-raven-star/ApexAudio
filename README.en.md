# Apex Audio

**Apex Audio lets the Flydigi Apex 6 Pro use selected game sounds as input for supplemental vibration through Flydigi X-Haptics.** When a game adapter can read dialogue, background music, and sound effects as separate groups, you can turn off dialogue and music while keeping footsteps, gunfire, and other effects as vibration input. The game continues playing all of its normal audio.
 Game-specific capture is supplied by `.gpack` adapters.

[中文说明](README.md)

## Downloads

GitHub Releases provide a Windows x64 application archive and individually downloadable game adapters. The application requires .NET 8 Desktop Runtime and WebView2 Runtime. Supplemental haptics are currently designed and verified primarily with the Flydigi Apex 6 Pro.

Import game adapters from the app as `.gpack` files. Each package only matches the game executable version declared in its manifest. Game updates may require an updated package. Supplemental haptics require a compatible official X-Haptics installation.

The application and game adapters are separate assets on the same Release page. Download the app and only the `.gpack` files for games you play. The source tree does not include recordings, user settings, or local game data.

## Develop an adapter

Start with [START-HERE.md](START-HERE.md), then read the [game-pack authoring guide](docs/authoring/gamepack-authoring.md). Generic templates and engine adapters are included; ready-to-import game packages are distributed through GitHub Releases.

## Build from source

Windows x64, PowerShell 7, and the .NET 8 SDK are required. See the [development guide](docs/development/adapter-maintenance.md) and [build environment](docs/development/build-environment.md).

```powershell
pwsh -File tools/Build.ps1 -Test
```

This project is licensed under the [MIT License](LICENSE). See [THIRD-PARTY.md](THIRD-PARTY.md) for separate terms.
