# Sensor map: Intel CPUs and NVIDIA GPUs

No reference dump is checked in yet for an Intel CPU or NVIDIA GPU. The code
uses LibreHardwareMonitor names rather than array positions, so these are the
names to look for when comparing a new dump:

| Value | Accepted LHM sensor names | Notes |
|---|---|---|
| Per-core load | `CPU Core #1`, `CPU Core #2`, … | Logical processors, including SMT threads |
| Per-core clock | `Core #1` (AMD) or `CPU Core #1` (some Intel builds) | `(Effective)` variants are ignored; they sit near zero at idle |
| Package temperature | `CPU Package`, `Core Average`, `Core (Tctl/Tdie)`, `Tdie` | Falls back to the first temperature sensor |
| Package power | `Package`, `PPT`, `CPU Package` | Summed across sockets when more than one is exposed |
| GPU load | `GPU Core`, `GPU Total`, `D3D 3D` | A missing load sensor is shown as `n/a` |
| GPU temperature | `GPU Core`, `GPU Temperature` | Hot spot/memory sensors are matched separately |
| VRAM used | `GPU Memory Used`, `D3D Dedicated Memory Used` | MB in LHM, converted to bytes |
| VRAM total | `GPU Memory Total`, `D3D Dedicated Memory Total` | Strict match, so “used” cannot be mistaken for “total” |
| GPU clock | `GPU Core`, `GPU Clock` | Memory clock: `GPU Memory`, `Memory Clock` |
| Fan | `GPU Fan` (RPM and control %) | `0` is a real passive-mode reading |

NVIDIA's `GPU Memory Junction` is used for the VRAM temperature when present.
Intel integrated GPUs often expose fewer clock and fan sensors; the dashboard
shows `n/a` instead of inventing values.

Multi-socket CPUs are supported: every CPU hardware gets its own per-core
entries, package temperature is the hottest socket, package power is summed and
the headline clock is averaged.

When reporting a missing value, attach the output of:

```powershell
vitals sensors --json > sensors.json
```

Include the exact CPU/GPU model and whether the process was elevated. A real
dump can then be added here as a reference map.
