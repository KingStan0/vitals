# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- `vitals setup` installs the optional PawnIO driver. It downloads the official
  installer from GitHub, verifies a pinned SHA-256, and runs the vendor's silent
  installer after consent. `--check`, `--yes`, `--no-start` and `--force` cover
  status checks, unattended installs and upgrades.
- An elevated dashboard offers the same install when it starts without PawnIO.
  The prompt can be disabled with `promptForPawnIo: false` and is skipped by
  `--no-driver`, `--render-once` and all headless commands.
- `watch --out FILE` writes the stream to a file.
- CI now fails on vulnerable NuGet packages and verifies formatting.
- Release packaging propagates the tag version to the binary and smoke-tests it.

### Changed

- The network and install policy is now explicit: `vitals` still has no
  telemetry and never bundles a driver, but the opt-in PawnIO setup may make one
  HTTPS request to the official GitHub release. Installation always requires
  consent, uses the verified installer, and never reboots automatically.
- Sampling reuses the warm-up snapshot instead of taking three samples, so
  one-shot commands perform one LibreHardwareMonitor update and report real
  CPU/disk/network rates on the first row.
- The TUI session buffer stores only timestamps and raw readings and is capped
  at 6,000 frames, bounding worst-case memory even with `historySeconds: 86400`.
- Drive and network-adapter topology is cached for 30 seconds; per-tick work is
  limited to free space and byte counters. Sensor identifiers are cached with
  weak keys, and readings are rounded when created to avoid a second allocation
  pass.
- Process start times are probed for new or suspect (recycled) PIDs and
  periodically thereafter, and CPU deltas use a monotonic clock instead of wall
  time.
- Network totals prefer physical adapters and fall back to virtual adapters on
  VM guests, avoiding VPN/Hyper-V double counting.
- Per-core clocks accept Intel's `CPU Core #N` names as well as AMD's `Core #N`;
  multi-socket package sensors are aggregated, and NVIDIA/Intel D3D VRAM totals
  are recognised.
- Zero fan RPM/percent is reported as a real reading (passive mode) instead of
  being rendered as missing.
- `watch` and `export` wake immediately on Ctrl+C and cap their final sleep at
  the remaining `--duration`, so short runs no longer overshoot.
- `vitals.cmd` detects headless commands in any argument position, treats
  `--render-once` and `setup --check` as headless, and waits for elevated
  `setup` runs so their exit codes reach scripts.
- Human-readable headless output and `--render-once` honour `--ascii` fully.
- The TUI refreshes when paused, when a toast appears or expires, and when peaks
  are reset.
- CSV export streams rows to disk instead of buffering the whole session, writes
  a UTF-8 BOM for Excel, and neutralises formula-like text fields.
- The session buffer uses a queue and a dynamic cap based on the current
  sampling interval.
- The dashboard sorts the full process list for the memory and name views
  instead of only the busiest CPU processes.
- `vitals sensors` warms up rate counters so disk and network readings are not
  reported as zero on the first sample.
- CI reads the vulnerability report as JSON instead of matching console text.
- Sensor selection follows the documented name priority rather than the order
  LibreHardwareMonitor enumerates sensors in.
- Drive counters are keyed by volume and reconciled when the drive topology
  changes, so a drive letter that appears or disappears no longer shifts
  throughput onto the wrong volume.
- `--help` and `--version` take precedence over other arguments, including
  invalid ones.

### Fixed

- First-frame CPU/disk/network rates were reported as zero because the rate
  window was reset immediately before sampling.
- The dashboard now rejects redirected stdin with guidance instead of spinning
  on unreadable key input, and exits cleanly if input disappears later.
- GPUs that expose no load sensor show `load n/a` instead of a fake idle 0%.
- Control characters in machine, sensor, process and status text are replaced
  before rendering, and the status row drops trailing items instead of wrapping
  on narrow terminals.
- `snapshot` and `sensors` report a status note when `GetSystemTimes` fails
  instead of silently showing zero CPU load.
- Sampler start/seed/dispose are serialized, `LastError` is published safely,
  and a disposed sampler cannot start or be seeded.
- Storage SMART updates are due on the first sample regardless of system uptime.
- `scripts/package.ps1 -Version dev` no longer forwards an invalid NuGet version
  and fails during restore; non-semantic labels are package-only.
- `bench.ps1`/`soak.ps1` prefer a source build over a potentially stale `dist`
  executable and print the resolved path.
- Short sensor names truncate on text elements, so surrogate pairs and combining
  sequences cannot be split.
- Disk write throughput was dropped when a volume reported zero reads.
- `History`/`Series` reads and writes are now synchronized for the UI thread.
- Sampler shutdown is bounded and disposes the sensor engine only after the
  sampling thread has stopped, so a stuck driver call cannot hang exit or race
  disposal.
- Duration parsing rejects invalid suffixes and values that overflow
  `TimeSpan` instead of crashing.
- Drive probing checks the drive type before `IsReady`, avoiding unnecessary
  network/removable device access.
- Storage and board temperature lists exclude warning/critical limit sensors.
- Long labels no longer overflow the temperature/process panels, and ASCII mode
  uses ASCII borders and truncation.
- Release tags are validated before packaging.
- `--no-driver` no longer sets `status.lhmError` or renders a red error; the
  mode is reported through `lhmAvailable` and the status notes.
- The PID-reuse guard no longer reports protected processes as permanently
  idle when their start time is unreadable.
- The CPU/GPU headlines, CPU detail row, memory row and GPU facts no longer
  wrap on narrow terminals.
- Label truncation counts display cells, so CJK and emoji labels cannot
  overflow the panel or split a surrogate pair.
- A GPU's memory junction temperature is no longer selected as its hot spot,
  and sparse multi-socket core indices no longer produce duplicate core IDs.
- PawnIO installed as a service or driver file is detected even when no
  uninstall registry entry exists.
- `--out`/`--config` reject empty or whitespace-only paths, and `--interval`
  is rejected for `snapshot`/`sensors` instead of being silently ignored.
- A sampler cannot be seeded twice, and `watch`/`export` warm up before
  creating their output file so a sensor failure leaves no empty file.
- Temperature chips reserve room for three-digit values, and header/status
  text is budgeted by display width so wide glyphs cannot overflow.
- `soak.ps1` retains the process handle so the exit-code check works under
  Windows PowerShell, and it fails when `watch` does not finish in time.

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
