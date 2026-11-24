# Changelog

## [1.0.0] - 2026-11-24

### Added
- Clean-room RIFX (`Egg!`) binary `.aep` parser and lossless round-trip writer (`VersionFlow.Formats`), preserving unknown chunks, odd-byte padding, and opaque `LIST:btdk` CoolType blocks.
- Hardened `.aepx` XML parser and writer with DTD/XXE protection and `<head>` / `<prin bdata>` / `<alas bdata>` support.
- Version detection engine separating internal `head` format versions (`93`–`97`) from After Effects 2020–2026 application releases (`17.x`–`26.x`).
- Compatibility analyzer and feature database (`VersionFlow.Compatibility`).
- Transactional conversion engine defaulting to **Compatibility Mode** (with **Safe Mode** and **Experimental Mode** also supported), migration rules (`VF-VERSION-METADATA`, `VF-3D-RENDERER`, `VF-TRACK-MATTE-COMPATIBILITY`, `VF-ASSET-RELINK`), SHA-256 verification, and HTML/JSON reports.
- Structural validation (`VersionFlow.Validation`) plus registry-based Adobe After Effects detection and isolated verification (`AfterFX.exe` scripting with `aerender.exe` live frame-0 fallback and `RenderComparisonService` fallback to `Multi-Machine Sequence`).
- WPF desktop application (`VersionFlow.exe`) with Splash screen, Dark/Light/System themes, custom vector icons, Convert, Batch Queue, History, Compatibility Matrix, Settings, Help, and About views.
- Official multi-resolution `VersionFlow.ico` and `VersionFlow.png` branding assets.
- Command-line tool (`versionflow.exe`) with `versions`, `inspect`, `analyse`, `convert`, `batch`, `validate`, and `verify-target` (community tester verification submission generator).
- Isolated out-of-process worker (`VersionFlow.Worker.exe`).
- GitHub Actions release workflow (`.github/workflows/build-and-release.yml`) and tester verification issue form (`.github/ISSUE_TEMPLATE/version-verification.yml`).
