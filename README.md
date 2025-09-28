<div style="display: flex; align-items: center; gap: 7px"><img src="https://github.com/tareqimbasher/netpad/blob/main/src/Apps/NetPad.Apps.App/wwwroot/logo/circle/32x32.png?raw=true" /> NetPad (Performance Optimized Fork)</div>

A cross-platform C# editor and playground with enhanced dependency caching and NuGet resolution.

> **Note**: This is a fork of the original [NetPad project](https://github.com/tareqimbasher/NetPad) with performance optimizations for script dependency resolution.

## 🚀 Performance Improvements

This fork includes significant performance improvements for NuGet dependency handling:

- **Per-Package Caching**: Individual NuGet packages are cached separately for optimal reuse across scripts
- **Smart Cache Strategy**: Dependencies are loaded from cache when available, avoiding repeated downloads
- **Data Connection Caching**: Database connection resources are cached to avoid regeneration
- **Async Parallel Processing**: Uses Task.WhenAll for concurrent dependency loading instead of blocking operations

[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/tareqimbasher/NetPad/build.yml)](https://github.com/tareqimbasher/NetPad/actions)
[![GitHub Release](https://img.shields.io/github/v/release/tareqimbasher/NetPad?color=%23097bbb)](https://github.com/tareqimbasher/NetPad/releases/latest)
![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/tareqimbasher/NetPad/total)
![GitHub commits since latest release](https://img.shields.io/github/commits-since/tareqimbasher/netpad/latest)
[![Discord](https://img.shields.io/discord/1121067424146522162?label=discord&color=%235864F2)](https://discord.gg/FrgzNBYQFW)

![](https://github.com/tareqimbasher/netpad/blob/main/docs/images/preview.png?raw=true)

## Get Started

NetPad is a C# playground that lets you run C# code instantly, without the hassle of creating and
managing projects. Open NetPad, start coding, hit Run, and see your output immediately. It's that
simple.

- **Prototyping and Testing:** Quickly prototype and test code snippets before incorporating them
  into your projects.
- **Data Visualization:** Visualize data interactively for better insights and analysis.
- **Database Queries:** Query databases using LINQ or SQL effortlessly.
- **Learn and Experiment:** Experiment with new C# features or start learning C# in an intuitive and
  accessible environment.
- **Utility Scripts:** Create and save your own utility or administration scripts for repeated use.

See [Features](https://github.com/tareqimbasher/NetPad?tab=readme-ov-file#features-rocket).

#### If you like this project, please star it 🌟 and consider [sponsoring](https://github.com/sponsors/tareqimbasher)!

## Motivation

We love LINQPad, but we miss its tremendous utility when working on non-Windows platforms.
This project aims to create an open-source, web-enabled, cross-platform alternative.

The goal isn't to reach 100% feature parity with LINQPad, but to offer an effective alternative that
covers features most commonly used and to introduce a few new useful ones.

## Requirements

The following must be installed to use NetPad:

* [.NET SDK](https://dotnet.microsoft.com/en-us/download) (v6 or later)

Additional requirement if you plan to create and use database connections:

* [EF Core tools](https://learn.microsoft.com/en-us/ef/core/cli/dotnet) (v6 or later)

## Download

### Official Installers

**[Download Now!](https://github.com/tareqimbasher/NetPad/releases)**

NetPad has 2 release channels:

- **Stable**: The Electron.js version of NetPad. Installers that start with `netpad`
- **vNext**: Uses a native Rust-based shell. Installers that start with `netpad_vnext`

Both channels have the same feature set. The native vNext version is lighter on system resources and
will eventually
become the main package. At which point, the Electron version will be deprecated.

> On **macOS**
> see [this](https://github.com/tareqimbasher/NetPad/wiki/Troubleshooting#netpad-is-damaged-and-cant-be-opened-you-should-move-it-to-the-trash)
> if you have trouble opening NetPad.

### Community Packages

These packages are maintained by community members.

| Installer                                                                                                                                                | Channel          | Command                                                |
| -------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------- | ------------------------------------------------------ |
| [![AUR Version](https://img.shields.io/aur/version/netpad-bin)](https://aur.archlinux.org/packages/netpad-bin)                                                | **stable** | `yay -S netpad-bin`                                  |
| [![WinGet Package Version](https://img.shields.io/winget/v/TareqImbasher.NetPad?color=%23097bbb)](https://winstall.app/apps/TareqImbasher.NetPad)             | **stable** | `winget install --id=TareqImbasher.NetPad  -e`       |
| [![WinGet Package Version](https://img.shields.io/winget/v/TareqImbasher.NetPad.vNext?color=%23097bbb)](https://winstall.app/apps/TareqImbasher.NetPad.vNext) | **vNext**  | `winget install --id=TareqImbasher.NetPad.vNext  -e` |

## Updates

NetPad automatically checks for updates each time you start the application and will notify
you when a new version is available.

Automatic updates are not supported, but will be added in the future to make updating
as seamless as possible. Stay tuned for future updates!

The latest version can be downloaded from
the [Releases](https://github.com/tareqimbasher/NetPad/releases) page.

## Wiki

The [Wiki](https://github.com/tareqimbasher/NetPad/wiki) is a great place to find more information
about NetPad.

## Troubleshooting

See the [Troubleshooting](https://github.com/tareqimbasher/NetPad/wiki/Troubleshooting) section of
the Wiki.

## About This Fork

This is a performance-optimized fork of NetPad that introduces intelligent dependency caching to improve script execution startup times.

### Key Changes

- **Enhanced Dependency Caching**: Added smart caching mechanism in `ClientServerScriptRunner.Setup.cs`
- **Cache Invalidation Strategy**: Implemented hash-based cache keys that automatically invalidate when dependencies change
- **Performance Improvements**: Significant reduction in script compilation time for complex dependency graphs

### Original Project

This fork is based on the excellent work by [Tareq Imbasher](https://github.com/tareqimbasher) and the NetPad community.
For the original project, please visit: [https://github.com/tareqimbasher/NetPad](https://github.com/tareqimbasher/NetPad)

## Contribution & Support

All Pull Requests, feedback and contributions are welcome! Please read
the [Contributing guidelines](./CONTRIBUTING.md) for more information about how to contribute and
build/run the project.

### Contributing to This Fork

If you'd like to contribute to the performance optimizations or suggest new improvements:

1. Fork this repository
2. Create a feature branch for your changes
3. Test your changes thoroughly
4. Submit a pull request with a clear description of the improvements

For general NetPad issues and feature requests, please consider contributing to the
[original project](https://github.com/tareqimbasher/NetPad).

A special thanks to NetPad's wonderful `<a href="https://github.com/sponsors/tareqimbasher">`
sponsors `</a>`. Sponsorships help pay for macOS builds and cross-platform testing and helps me
maintain this project.

`<a href="https://github.com/mattjcowan"><img src="https://github.com/mattjcowan.png" width="50px" alt="mattjcowan" />``</a>`
&nbsp;&nbsp;`<a href="https://github.com/lpreiner"><img src="https://github.com/lpreiner.png" width="50px" alt="lpreiner" />``</a>`
&nbsp;&nbsp;`<a href="https://github.com/ChristopherHaws"><img src="https://github.com/ChristopherHaws.png" width="50px" alt="ChristopherHaws" />``</a>`
&nbsp;&nbsp;`<a href="https://github.com/OddSkancke"><img src="https://github.com/OddSkancke.png" width="50px" alt="OddSkancke" />``</a>`
&nbsp;&nbsp;`<a href="https://github.com/SimonNyvall"><img src="https://github.com/SimonNyvall.png" width="50px" alt="SimonNyvall" />``</a>`
&nbsp;&nbsp;

If you enjoy using NetPad and would like to support its continued development,
consider [sponsoring](https://github.com/sponsors/tareqimbasher) the project. A small contribution
helps immensely with maintenance and the addition of new features.
Thank you for your support! ❤️

## Discord

Join the [Discord server](https://discord.gg/FrgzNBYQFW) to collaborate, ask questions and get the
latest announcements!

## Features 🚀

* The basics:
  * Write, save and run your own scripts.
  * Manage namespaces.
  * Standard code editor features powered by Monaco editor.
  * Auto-open unsaved scripts from previous session on launch.
* Dump complex objects to the results console and export results to Excel or HTML.
* Choose the .NET SDK version you want to use per script.
* Add database connections and query them with LINQ or T-SQL.
* Add NuGet packages and reference assemblies from disk.
* Vim keybindings.
* Syntax Tree Visualizer.
* User-defined results styling.
* LSP powered by OmniSharp:
  * Code Completion (Intellisense)
  * Semantic Highlighting
  * CodeLens
  * Inlay Hints
  * Hover for Documentation
  * Go-to implementation
  * Find References
  * Find Symbol
  * Rename Symbol
  * Action Suggestions
  * Diagnostics
  * Document Highlighting
  * Contextual code folding
  * Format document/selection/on-type

## Roadmap 🚧

* Debugging
* Support for more database providers (Oracle, Mongo...etc)
* Hyperlink driven Lazy-loading of results, and a DataGrid view
* Benchmark your code
* Referencing other scripts
* Ability to run a script from the command-line
* IL Viewer
* Export a script as a "ready to run" .NET app
* Export a script as a C# project
* Git tracking of script changes
* Workspaces/Sessions
* Plugins

<br/>
<br/>
<img src="https://api.star-history.com/svg?repos=tareqimbasher/NetPad&type=Date" />
<br/>
<br/>

## Tech Stack 💻

* .NET
* ElectronSharp ([github](https://github.com/theolivenbaum/electron-sharp)) for the Electron shell
  desktop version
* Tauri ([docs](https://tauri.app/)) for the native shell desktop version
* Aurelia 2 ([docs](https://docs.aurelia.io/))

### How it works

NetPad runs an ASP.NET web app that hosts a web user interface. It can be
packaged as a desktop app or served and accessed on any browser.

Communication between the user interface and the ASP.NET backend occurs via HTTP
and SignalR.

## Build

See [CONTRIBUTING.md](./CONTRIBUTING.md) for instructions on how to build and run NetPad from
source. NetPad can be run as a desktop app or as a web application accessed with a web browser.

## Resources 📚:

* Technical Docs: [Go](https://tareqimbasher.github.io/NetPad)
* Build & RUn: [Go](./CONTRIBUTING.md)
