# DoctorDump

DoctorDump is a Windows crash diagnostics project for native and .NET desktop applications. The MVP captures process dumps, stores metadata, and generates developer-friendly reports.

## Project Layout

```text
src/
  DoctorDump.Agent/      C++ Win32 dump capture CLI
  DoctorDump.Core/       Shared .NET contracts and paths
  DoctorDump.Reporter/   HTML report generator
  DoctorDump.UI/         WPF desktop shell
samples/
  SampleCrashingApp/     Demo app with intentional crashes
docs/
  LLD.md                 Low-level design
```

## MVP Commands

```powershell
DoctorDump.Agent.exe list --json
DoctorDump.Agent.exe capture --pid 1234 --type mini --output "$env:LOCALAPPDATA\DumpDoctor\dumps"
DoctorDump.Reporter.exe --metadata metadata.json --analysis analysis.json --output report.html
```

## Build Notes

- Build .NET projects with `dotnet build`.
- Build `DoctorDump.Agent` from Visual Studio Developer Command Prompt or Visual Studio because it needs the VC++ toolchain and `Dbghelp.lib`.
- VS Code tasks are included. Use `Terminal > Run Build Task` and choose `build: all`.
- The WPF UI expects the agent at `src\DoctorDump.Agent\x64\Debug\DoctorDump.Agent.exe` during local development.

## Roadmap

- Add crash monitoring mode.
- Add `cdb.exe`/WinDbg-based analyzer.
- Generate reports automatically after capture.
- Add settings for symbol path and dump retention.
- Add Azure upload and team dashboard after the local MVP is stable.
