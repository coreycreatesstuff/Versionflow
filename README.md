# VersionFlow

**Create forward. Convert backward.**

VersionFlow is an offline-first Windows desktop utility and CLI tool for inspecting, analysing, and converting Adobe After Effects (`.aep` / `.aepx`) project files across **After Effects 2020 through After Effects 2026** while preserving project structure and leaving original files untouched.

> **Independence Notice:** VersionFlow is an independent application and is not affiliated with, endorsed by, or officially associated with Adobe Inc.

---

## Features

- **Supported Releases (`AE 2020 – AE 2026`):** Supports Adobe After Effects `17.x` through `26.x` (internal `.aep` format versions `93`–`97`).
- **Lossless Binary `.aep` (RIFX / `Egg!`) Parser & Writer:** Preserves every chunk (known and unknown, including opaque `LIST:btdk` text blocks), parent-child container hierarchy, odd-byte alignment padding, and trailing bytes.
- **Hardened `.aepx` XML Engine:** Validates `AfterEffectsProject` XML with DTD/XXE disabled, updates `<head>` / `<prin bdata>` / `<alas bdata>` payloads, and preserves unknown attributes and elements.
- **Honest Version Detection:** Separates application release years (`2020`–`2026`) from internal `head` format versions (`93`–`97`), reporting exact ranges when multiple releases share one format family (`AE 2020–2022` for Format `93`).
- **Compatibility Analyser & Migration Rules:** Scans 3D renderers (`ADBE Advanced 3D` → `ADBE Escher` Classic 3D fallback), Format `94+` selectable track matte boundaries, built-in & third-party effects, expression API compatibility (`previousKey()`, `nextKey()`, `setFont()`), color management (`CPPl`), and unmapped binary chunks.
- **Compatibility Mode Default + Safety Controls:** Defaults to rule-backed **Compatibility Mode** (with **Safe Mode** and **Experimental Mode** also supported), atomic temp-file writes, SHA-256 source integrity checks, collision-safe filenames (`_AE2020`), HTML/JSON conversion reports, and optional live verification via installed After Effects (`AfterFX.exe` / `aerender.exe`).
- **Desktop App + CLI + Isolated Worker:** Includes the WPF GUI (`VersionFlow.exe`), isolated conversion worker (`VersionFlow.Worker.exe`), and CLI (`cli\versionflow.exe`).

---

## Current Verification Status

VersionFlow never claims target opening is `Verified` until converted outputs have actually been opened and tested in that real After Effects version:

| Release | App Version | Format Major | Current Status | Notes |
|---|---|---|---|---|
| **AE 2026** | `26.x` | `97` | **Experimental** | Rule-backed conversion; awaiting live AE 2026 tester verification |
| **AE 2025** | `25.x` | `96` | **Experimental** | Rule-backed conversion; awaiting live AE 2025 tester verification |
| **AE 2024** | `24.x` | `95` | **Partially Verified** | Verified on `secret.aep` round-trip (SHA-256 identical) + live `aerender.exe 24.4x47` frame render; awaiting curated `25/26 → 24` project matrix |
| **AE 2023** | `23.x` | `94` | **Experimental** | Rule-backed conversion; awaiting live AE 2023 tester verification |
| **AE 2022** | `22.x` | `93` | **Experimental** | Shares Format `93` with AE 2020–2021; awaiting live AE 2022 tester verification |
| **AE 2021** | `18.x` | `93` | **Experimental** | Shares Format `93` with AE 2020 & AE 2022; awaiting live AE 2021 tester verification |
| **AE 2020** | `17.x` | `93` | **Experimental** | Flagship downgrade target; awaiting live AE 2020 tester verification |

---

## Help Verify Other AE Versions (Community Testing)

If you have **AE 2020, 2021, 2022, 2023, 2025, or 2026** installed, you can help promote that release from `Experimental` to verified status in under a minute without modifying your original projects:

1. Download and extract `VersionFlow-win-x64-portable.zip` (or install via `VersionFlow-Setup.exe`) from **Releases**.
2. Run live target verification against **a copy** of a test project (or omit the path to use your AE installation's built-in `Required\secret.aep` if present):
   ```powershell
   .\cli\versionflow.exe verify-target "C:\Path\To\TestProject.aep" --report verification-report.json
   ```
3. Private directory paths are stripped from `verification-report.json` by default (`--include-paths` to include them).
4. Open a **Version Verification Report** issue on GitHub and paste the generated JSON output + what features were tested.

### Promotion Criteria
- **`Partially Verified`:** Genuine `.aep` fixture from that release + clean structural round-trip AND at least one live open/render confirmation in that target After Effects version.
- **`Verified`:** Verified bidirectional/downgrade conversions across representative compositions (keyframes, expressions, Classic 3D, track mattes, effects) confirmed in the target After Effects release.

---

## Build From Source

Prerequisites: **.NET 10 SDK** (`10.0.x`), Windows 10/11 (`x64`).

```powershell
dotnet publish src/VersionFlow.App/VersionFlow.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o dist/win-x64
dotnet publish src/VersionFlow.Cli/VersionFlow.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o dist/win-x64/cli
dotnet publish src/VersionFlow.Worker/VersionFlow.Worker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o dist/win-x64
```
