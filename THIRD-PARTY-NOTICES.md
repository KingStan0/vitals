# Third-party notices

vitals is MIT licensed. It builds on the following open-source projects.

## LibreHardwareMonitorLib

- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- License: Mozilla Public License 2.0 (MPL-2.0)
- Used for: CPU, GPU, motherboard, fan and storage sensors.
- Notes: MPL-2.0 is file-level copyleft. vitals consumes the library as an
  unmodified NuGet package. If you modify LibreHardwareMonitorLib, those
  modified files must remain under MPL-2.0.

## Spectre.Console

- Project: https://github.com/spectreconsole/spectre.console
- License: MIT
- Used for: terminal rendering, layout, markup and colour.

## .NET 8 runtime

- Project: https://github.com/dotnet/runtime
- License: MIT
- Used for: runtime and base class library. The self-contained publish output
  redistributes the runtime under its original license.

## PawnIO (optional, not bundled)

- Project: https://pawnio.eu / https://github.com/namazso/PawnIO
- Used for: signed kernel driver that provides low-level CPU and motherboard
  sensor access to LibreHardwareMonitor.
- vitals does not bundle, download or install PawnIO at runtime. It only
  detects whether PawnIO is already installed and points the user at it.
  Refer to the upstream project for its licensing terms.

## Test-only dependencies

- xUnit: Apache-2.0
- Spectre.Console.Testing: MIT
- coverlet: MIT
