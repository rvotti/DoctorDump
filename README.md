# DoctorDump

DoctorDump is a Windows crash diagnostics project for native and .NET desktop applications. The MVP captures process dumps, stores metadata, and generates developer-friendly reports.

## Current Features

- List running Windows processes.
- Capture a manual mini dump from a selected process.
- Monitor a process for an unhandled crash and capture at the second-chance exception.
- Launch an executable under crash monitoring.
- Import and analyze an existing `.dmp` file.
- Generate HTML reports with stack frames, exception details, recommendations, and raw debugger output.
- Extract managed .NET exception type, message, and managed call stack through SOS when available.
- Open reports, open dump folders, re-analyze, and delete local history entries from the WPF UI.
- Configure dump type, output directory, symbol path, auto-analysis, and retention from the WPF UI.

## Project Layout

```text
src/
  DoctorDump.Agent/      C++ Win32 dump capture CLI
  DoctorDump.Analyzer/   cdb.exe/Windows Debugging Tools analyzer
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
DoctorDump.Agent.exe monitor --pid 1234 --type mini --output "$env:LOCALAPPDATA\DumpDoctor\dumps"
DoctorDump.Agent.exe launch --exe "C:\Apps\App.exe" --args "--crash" --type mini --output "$env:LOCALAPPDATA\DumpDoctor\dumps"
DoctorDump.Analyzer.exe --dump file.dmp --dump-id "<guid>" --output "<capture-folder>" --exception-code 0xE0434352
DoctorDump.Analyzer.exe --self-test
DoctorDump.Reporter.exe --metadata metadata.json --analysis analysis.json --output report.html
```

## Build Notes

- Build .NET projects with `dotnet build`.
- Build `DoctorDump.Agent` from Visual Studio Developer Command Prompt or Visual Studio because it needs the VC++ toolchain and `Dbghelp.lib`.
- VS Code tasks are included. Use `Terminal > Run Build Task` and choose `build: all`.
- The WPF UI expects the agent at `src\DoctorDump.Agent\x64\Debug\DoctorDump.Agent.exe` during local development.
- Analyzer Phase 1 uses `cdb.exe` from Debugging Tools for Windows. If it is not installed, run the Windows SDK installer with `OptionId.WindowsDesktopDebuggers`. The analyzer writes a graceful `DebuggerNotFound` result when `cdb.exe` is unavailable.
- Managed .NET analysis uses SOS through `cdb.exe` commands `.loadby sos coreclr`, `!pe`, and `!clrstack -f`.

## Local Settings

DoctorDump stores settings at:

```text
%LOCALAPPDATA%\DumpDoctor\config\settings.json
```

The WPF settings row controls dump type, output folder, symbol path, automatic analysis, and retention days.

## Roadmap

- Add crash monitoring mode.
- Improve `cdb.exe` parser coverage for more crash shapes.
- Add launch argument support in the WPF UI.
- Improve managed source-line resolution with private PDB paths.
- Add settings for symbol path and dump retention.
- Add Azure upload and team dashboard after the local MVP is stable.
