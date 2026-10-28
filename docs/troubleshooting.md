# Troubleshooting

Start with these commands:

```powershell
.\vitals.exe --version
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

- Launch the dashboard with `vitals.cmd` and accept the UAC prompt.
- Install PawnIO from <https://pawnio.eu>. `vitals` does not install it.
- Confirm `vitals snapshot --json` reports `"pawnIoInstalled": true` and
  `"elevated": true`.
- Some boards expose no NCT6687D-compatible chip. In that case only CPU/GPU
  sensors will appear.

`vitals snapshot --json` works without elevation; missing values are `null`, not
fake zeros.

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
