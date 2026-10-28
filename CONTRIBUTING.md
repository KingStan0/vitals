# Contributing to vitals

Thanks for helping make a small, fast Windows monitor better. Bug reports,
sensor dumps and focused pull requests are all welcome.

## Reporting a bug

Open an issue with:

- `vitals --version`
- Windows version and whether the terminal is elevated
- CPU, GPU, motherboard and storage models
- the output of `vitals sensors --json` (attach it as a file if it is large)
- what you expected and what happened

Redact hostnames, usernames or serial numbers if you prefer. A screenshot or a
copy of the dashboard helps for layout bugs.

Please do **not** paste driver binaries, full memory dumps or secrets. Report
security-sensitive issues through the process in [SECURITY.md](SECURITY.md),
not a public issue.

## Development setup

Requirements:

- Windows 10 1809+ or Windows 11, x64
- [.NET 10 SDK](https://dot.net) (the repository pins 10.0.402 in `global.json`)
- Optional: [PawnIO](https://pawnio.eu) and an elevated terminal for full
  sensor coverage

```powershell
git clone https://github.com/KingStan0/vitals.git
cd vitals
dotnet build
dotnet test
dotnet run --project src/Vitals -- --render-once
```

Package and smoke-test a release build:

```powershell
pwsh -File scripts/package.ps1 -Version dev
```

## Pull requests

- Keep changes focused. Separate refactors from behavior changes.
- Add or update tests for parsing, formatting, JSON shape, config handling and
  sensor selection logic.
- Run `dotnet build` and `dotnet test` before opening the PR.
- Do not add a bundled kernel driver. `vitals` detects PawnIO; it never ships or
  installs one.
- Treat the JSON output as a public API. If a field changes, update
  `README.md`, `docs/schema/snapshot.schema.json` and the changelog.
- Match the existing style: file-scoped namespaces, nullable enabled, four-space
  indentation, and small focused methods.
- New dependencies need a clear reason and a compatible license. Add them to
  `THIRD-PARTY-NOTICES.md` and `licenses/` when they are distributed.

## Sensor reports

The mappings in `docs/sensors/` are built from real `vitals sensors --json`
dumps. If a temperature, fan or clock is missing on your board, include the dump
and the exact sensor name from LibreHardwareMonitor. Curated values are selected
by sensor name; raw readings retain LHM's stable identifier. Never guess a
sensor by array position.

## Commit messages

Use a short imperative subject, for example:

```text
Add AMD Zen 4 package temperature mapping
Fix CSV quoting for sensor names containing quotes
```

## License

By contributing, you agree that your contribution is licensed under the
project's MIT license.
