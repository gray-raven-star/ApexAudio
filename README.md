# Apex Audio

Windows tool for independent game-audio capture. The host manages game-pack installation, group presets, multitrack recording, playback, and X-Haptics comparison. Game-specific capture belongs in separate `.gpack` adapters.

## Downloads

- **Windows app:** download the `ApexAudio-Windows-x64` archive from GitHub Releases.
- **Game-adapter development kit:** download `ApexAudio-DevKit`; it includes the workflow, adapter protocol, templates, diagnostics, and compiler tools, but no existing target-game answers.

## Develop a game adapter

Start with [START-HERE.md](START-HERE.md), then read [the game-pack authoring guide](docs/gamepack-authoring.md). Adapter and host boundaries are described in [the architecture guide](docs/architecture.md). The `gamepacks/` directory is intentionally empty so independent evaluations start without existing game-specific solutions.

A package archive or successful build is not proof of game support. Follow the validation checklist and clearly label what was and was not verified.

## Build from source

Windows x64, PowerShell 7, and .NET 8 SDK are required. See [Build.ps1](tools/Build.ps1) and the developer documentation. Release archives are assembled with [Release.ps1](tools/Release.ps1).

This project is licensed under the [MIT License](LICENSE). Third-party components and their separate terms are listed in [THIRD-PARTY.md](THIRD-PARTY.md).

