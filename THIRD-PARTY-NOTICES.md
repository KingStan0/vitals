# Third-party notices

vitals is MIT licensed. It builds on the following open-source projects. Full
license texts are included in the release zip under `licenses/`.

## Runtime dependencies

### Mozilla Public License 2.0

Full text: [`licenses/MPL-2.0.txt`](licenses/MPL-2.0.txt)

- **LibreHardwareMonitorLib 0.9.6** — CPU, GPU, motherboard, fan and storage
  sensors. <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor>
- **BlackSharp.Core 1.0.7** — transitive LibreHardwareMonitor dependency.
  <https://github.com/Blacktempel/BlackSharp>
- **DiskInfoToolkit 1.1.2** — transitive storage-info dependency.
  <https://github.com/Blacktempel/DiskInfoToolkit>
- **RAMSPDToolkit-NDD 1.4.2** — transitive memory/SPD dependency.
  <https://github.com/Blacktempel/RAMSPDToolkit>

MPL-2.0 is file-level copyleft. vitals consumes these libraries as unmodified
NuGet packages. If you modify their files, those modified files must remain
under MPL-2.0. The corresponding source is available from the links above.

### MIT

Full text: [`licenses/MIT.txt`](licenses/MIT.txt)

- **Spectre.Console 0.57.2** and **Spectre.Console.Ansi 0.57.2** — terminal
  rendering, layout, markup and colour.
  <https://github.com/spectreconsole/spectre.console>
- **Mono.Posix.NETStandard 1.0.0** — POSIX compatibility used by a transitive
  dependency. <https://github.com/mono/mono>
- **System.CodeDom 10.0.2**, **System.IO.FileSystem.AccessControl 5.0.0**,
  **System.IO.Ports 10.0.3**, **System.Management 10.0.2**, and the
  `runtime.*.runtime.native.System.IO.Ports` packages — .NET runtime libraries.
  <https://github.com/dotnet/runtime>
- **.NET 10 runtime** — runtime and base class library. The self-contained
  publish output redistributes the runtime under its original license.
  <https://github.com/dotnet/runtime>

### Apache License 2.0

Full text: [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt)

- **HidSharp 2.6.4** — HID access used by LibreHardwareMonitor.
  Copyright 2010–2025 James F. Bellinger.
  <https://software.seekye.com/hidsharp>
  <https://github.com/IntergatedCircuits/HidSharp>

## Optional, not bundled

### PawnIO

- Project: <https://pawnio.eu> / <https://github.com/namazso/PawnIO>
- Used for: signed kernel driver that provides low-level CPU and motherboard
  sensor access to LibreHardwareMonitor.
- vitals does not bundle, download or install PawnIO at runtime. It only
  detects whether PawnIO is already installed and points the user at it.
  Refer to the upstream project for its licensing terms.

## Test-only dependencies

These are not distributed in the release zip:

- xUnit — Apache-2.0
- Spectre.Console.Testing — MIT
- coverlet — MIT
- Microsoft.NET.Test.Sdk — MIT
