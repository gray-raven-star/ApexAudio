# Apex Audio

Apex Audio captures separate game audio tracks on Windows. Choose which sounds feed Flydigi X-Haptics for supplemental vibration on the Flydigi Apex 6 Pro, or record the tracks and listen to them individually.

[中文说明](README.md)

## Download and use

1. Download the Windows x64 ZIP from the [latest stable release](https://github.com/gray-raven-star/ApexAudio/releases/latest). Extract it and run `ApexAudio.exe`. The app requires the .NET 8 Desktop Runtime and WebView2 Runtime. Supplemental vibration requires a supported official X-Haptics installation.
2. Pick a `.gpack` from the [gamepack downloads](docs/user/gamepack-downloads.md).
3. Follow the [guide to importing, connecting and recording](docs/user/connect-and-record.md).

## Build a gamepack

Work from the directory containing `ApexAudio.sln`. Set up the [build environment](docs/development/build-environment.md). This repository provides capture adapters, a copyable definition example in [gamepack-starter](gamepack-starter/README.md), and build tools. Individual game definitions are maintained by their authors.

1. [Identify the game's audio system](docs/authoring/identify-audio-system.md) and choose a capture adapter.
2. [Build a gamepack](docs/authoring/build-a-gamepack.md) from your game definition.
3. [Verify capture and feedback](docs/authoring/verify-a-gamepack.md) and record the test coverage.
4. [Release the gamepack](docs/authoring/release-a-gamepack.md) with compatibility notes and validation results.

See [AGENTS.md](AGENTS.md) for repository instructions used by coding agents.

## Develop the application

Use the [build guide](docs/development/build-environment.md) to build and run offline tests. See the [architecture and code entry points](docs/development/architecture.md) and the [documentation index](docs/README.md). Read the [contribution guidelines](CONTRIBUTING.md) before submitting changes.

This project is licensed under the [MIT License](LICENSE). See [THIRD-PARTY.md](THIRD-PARTY.md) for third-party terms.
