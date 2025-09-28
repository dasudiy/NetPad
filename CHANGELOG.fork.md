# Fork Changelog

This document tracks changes made in this fork of NetPad.

## [Per-Package Caching] - 2025-09-28

### Changes

- **Per-Package Dependency Caching**: NuGet packages now cached individually for better reuse
- **Data Connection Caching**: Database connection resources cached separately
- **Async Processing**: Improved concurrent dependency loading with Task.WhenAll

### Technical Implementation

- Added `CachedPackageDependencies` for individual package storage
- Added `CachedDataConnectionInfo` for data connection resources
- Enhanced `GatherDependenciesAsync` with per-package cache loading
- Separate cache directories: `PackageDependencies/` and `DataConnections/`

### Cache Structure

```
~/.local/share/NetPad/Cache/Packages/
├── NuGet/                     # Package binaries
├── PackageDependencies/       # Per-package dependency cache
└── DataConnections/           # Data connection resources
```

---

Fork based on [NetPad](https://github.com/tareqimbasher/NetPad) by [Tareq Imbasher](https://github.com/tareqimbasher)
