# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-10-22

### Added

- `--version` flag and `status.appVersion` in JSON output.
- `gpus` array in JSON snapshots. All adapters are exported; `gpu` remains the
  primary adapter shown in the dashboard.
- GitHub build-provenance attestations for release zips.
- Full dependency license texts in `licenses/`, included in release packages.
- `config.example.json`, JSON schema, troubleshooting guide and community files.
- Release smoke tests for the packaged executable.
- Regression tests for rounding, JSON shape, CSV escaping, config clamping and
  sensor filters.
- `scripts/soak.ps1` for steady-state CPU and memory measurements.
- Automated vulnerability audit and CodeQL scanning.

### Changed

- Retargeted the app and tests to .NET 10 LTS (`net10.0-windows`); the SDK is
  pinned in `global.json`.
- Release assets are versioned: `vitals-<version>-win-x64.zip`.
- Sensor values are rounded to the precision used by the dashboard (temperatures
  and power to 0.1, clocks/bytes to whole numbers, process CPU to 0.01) to remove
  floating-point noise from JSON.
- `vitals.cmd` now elevates only the interactive dashboard. Headless commands
  stay in the current console so pipes and redirection work.
- Dashboard and snapshot now report Windows 11 correctly on build 22000+.
- Invalid threshold config values are clamped instead of producing broken
  colours.
- Status notes from startup are merged with per-sample notes instead of being
  dropped.

### Fixed

- Windows 11 being labelled Windows 10 in the header.
- JSON sensor `kind` values now serialize as documented strings (`"Load"`,
  `"Temperature"`, ...) instead of numbers.
- Missing readings remain `null`; no zero-filled placeholders.
- Release packages now include `README.md`, `LICENSE`,
  `THIRD-PARTY-NOTICES.md`, `licenses/` and `config.example.json`.

## [0.1.0] - 2026-10-20

### Added

- Initial public release.
- Live Spectre.Console dashboard with CPU, per-core load, GPU, memory, volumes,
  disk/network throughput, temperatures, fans and top processes.
- `snapshot`, `watch`, `export`, `sensors`, `--help` and `--render-once`.
- JSON snapshot and NDJSON watch output; long-format CSV export.
- LibreHardwareMonitor-backed sensors with PawnIO detection and guidance.
- Config file with thresholds and dashboard preferences.
- Windows x64 self-contained single-file build and release zip.
- Sensor mapping notes for AMD Ryzen 5 5600G + Radeon RX 5700.

[0.2.0]: https://github.com/KingStan0/vitals/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/KingStan0/vitals/releases/tag/v0.1.0
