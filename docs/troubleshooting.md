# Troubleshooting

Start with these commands:

```powershell
.\vitals.exe --version
.\vitals.exe setup --check
.\vitals.exe snapshot
.\vitals.exe sensors --json > sensors.json
```

Attach `sensors.json` to a bug report if sensors are missing. It contains sensor
names and values only; review it before publishing.

## Windows says "Windows protected your PC"

The executable is not code-signed yet. Choose **More info → Run anyway**, or
build from source. Verify the download against the release's `.sha256` file and
GitHub provenance attestation first:

```powershell
gh attestation verify .\vitals-*-win-x64.zip --repo KingStan0/vitals
```

Do not disable SmartScreen or Defender globally.

## No CPU package temperature or motherboard sensors

Those sensors need both an elevated process and a PawnIO installation.

- Launch the dashboard with `vitals.cmd` and accept the UAC prompt, then accept
  the first-launch PawnIO prompt. Alternatively run `vitals setup` from any
  terminal; it requests elevation itself.
- Confirm `vitals setup --check` reports that PawnIO is installed, or that
  `vitals snapshot --json` reports `"pawnIoInstalled": true` and
  `"elevated": true`.
- If PawnIO was just installed and the installer asked for a restart, restart
  Windows before expecting the sensors.
- Some boards expose no NCT6687D-compatible chip. In that case only CPU/GPU
  sensors will appear.

`vitals snapshot --json` works without elevation; missing values are `null`, not
fake zeros.

## PawnIO setup fails

`vitals setup` downloads the installer from the official GitHub release and
verifies its SHA-256 before running it. If the download or install fails, the
message says which step failed.

- **Network or proxy errors.** The download needs HTTPS access to
  `github.com` and GitHub's release-asset host (currently
  `release-assets.githubusercontent.com`). Configure the system proxy, or
  download `PawnIO_setup.exe` from
  <https://github.com/namazso/PawnIO.Setup/releases> and install it manually.
  `vitals` never falls back to an unverified mirror.
- **SHA-256 mismatch.** The downloaded file did not match the pinned hash and
  was not run. This can mean a corrupted download, an intercepting proxy, or a
  changed upstream release. Retry; the rejected temporary file is deleted
  automatically. If the mismatch persists, report it as a security issue
  instead of bypassing the check.
- **UAC cancelled or denied.** `vitals setup` needs administrator rights. Run it
  again and accept the prompt, or start an elevated terminal. `setup --check`
  does not need elevation.
- **Antivirus or group policy blocks the installer.** Some managed machines
  block unknown installers or driver installation. Ask your administrator, or
  install PawnIO manually from the official release page.
- **Restart required.** If the installer returns `3010` or `1641`, PawnIO is
  installed but Windows must restart before the driver is active. `vitals` never
  restarts automatically.
- **Installer reports success but PawnIO is not detected.** Check the Windows
  installer logs, reboot, and run `vitals setup --check` again. If it still
  fails, include the `--debug` output in a bug report.

## No disk throughput

Volume throughput uses `IOCTL_DISK_PERFORMANCE` on volume handles and needs an
elevated process. Run the dashboard through `vitals.cmd`, or run the headless
command from an elevated terminal. The dashboard shows `disk R 0 B/s` when the
counters are unavailable.

## Network totals differ from Task Manager

`vitals` sums physical Ethernet/Wi-Fi adapters. Obvious virtual adapters (VPN,
Hyper-V, VirtualBox, WireGuard) are ignored when a physical adapter is present,
because they often carry a copy of the same bytes. On a VM guest, where virtual
adapters are the only network path, they are included instead.

## Intel or NVIDIA sensors look incomplete

Per-core clocks are matched by LibreHardwareMonitor names `Core #N` (AMD) and
`CPU Core #N` (some Intel builds); the `(Effective)` variants are ignored
because they sit near zero at idle. VRAM totals accept `GPU Memory Total` and
`D3D Dedicated Memory Total`. If a value is missing, attach
`vitals sensors --json`; the raw names let the mapping be extended without
guessing. Multi-socket CPUs aggregate package temperature/power and keep every
socket's cores.

## GPU sensors are missing on a laptop

The dashboard shows the primary adapter, chosen by VRAM size and power. Check
the JSON `gpus` array for all adapters. If the discrete GPU is idle or powered
down, LibreHardwareMonitor may not expose its sensors until it is active.

## Temperatures appear frozen or stale

Storage sensors are refreshed every five seconds by design to reduce I/O. Other
sensors update every sampling interval. Try `--interval 500` for a faster
dashboard, keeping in mind that some motherboard controllers update slowly.

## The dashboard looks broken

- Use [Windows Terminal](https://aka.ms/terminal) for Unicode gauges and colour.
- Run `.\vitals.exe --render-once` to see one frame without starting the TUI.
- Add `--ascii` for consoles without box-drawing glyphs.
- Add `--no-color` if colours are unreadable or your terminal does not support
  them.
- The layout stacks panels below 100 columns; widen the window or increase the
  font size if panels look cramped.

## "config not found" or values are ignored

The default config path is `%APPDATA%\vitals\config.json`. Pass an explicit file
with `--config .\config.json`, or copy `config.example.json` from the release zip.
Invalid intervals and counts are clamped to safe ranges; invalid JSON falls back
to defaults with a warning.

## High CPU or memory use

Sampling defaults to 1 Hz. Use `--interval 2000` for a slower dashboard, and
reduce `processCount` in the config if process enumeration is expensive. From a
source checkout, measure steady-state use with:

```powershell
pwsh -File scripts/soak.ps1 -Seconds 60
```

If the TUI is much more expensive than the headless soak, include both numbers
and the terminal you use in a bug report.

## Build errors: SDK not found or wrong version

Install the [.NET 10 SDK](https://dot.net). The repository pins 10.0.402 in
`global.json`; `dotnet --list-sdks` should show a 10.0.x SDK. If `dotnet` is not
on `PATH`, run `scripts/publish.ps1`, which checks the default install location.

## ARM64 Windows

The published release is x64. ARM64 Windows can run the x64 build under
emulation, but sensor access may be incomplete. A native arm64 build is not
published yet; see the issue tracker if you can test one.

## Still stuck

Open an issue with `vitals --version`, the JSON snapshot, the sensor dump and a
short description. For security-sensitive reports, use
[SECURITY.md](../SECURITY.md) instead.
