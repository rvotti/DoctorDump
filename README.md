# DoctorDump

DoctorDump is a Windows crash diagnostics tool for native and .NET desktop applications. It captures dumps, analyzes them with Windows Debugging Tools, extracts managed exception details with SOS, and generates developer-friendly HTML reports.

## Why It Exists

Windows crash debugging is still too manual for many small teams maintaining Win32, MFC, WPF, WinForms, or .NET desktop software. DoctorDump turns a vague report like "the app crashed" into a local diagnostic package with dump metadata, exception details, call stacks, raw debugger output, and recommended next actions.

## Current Capabilities

- List running Windows processes.
- Capture manual mini/full dumps from selected processes.
- Monitor an existing process and capture on second-chance crash exceptions.
- Launch an executable under crash monitoring.
- Import and analyze an existing `.dmp` file.
- Analyze dumps with `cdb.exe` and Windows Debugging Tools.
- Extract managed .NET exception type, message, and managed call stack through SOS.
- Generate HTML reports with native stack, managed stack, recommendations, and raw debugger output.
- Open reports, open dump folders, re-analyze, and delete local history entries from the WPF UI.
- Configure dump type, output directory, symbol path, auto-analysis, and retention from the WPF UI.

## Two-Minute Demo

1. Build everything:

```powershell
dotnet build DoctorDump.slnx
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" src\DoctorDump.Agent\DoctorDump.Agent.vcxproj /p:Configuration=Debug /p:Platform=x64
```

2. Launch the WPF UI:

```powershell
dotnet run --project src\DoctorDump.UI\DoctorDump.UI.csproj
```

3. Click **Launch App** and select:

```text
samples\SampleCrashingApp\bin\Debug\net10.0\SampleCrashingApp.exe
```

4. For CLI demo, run:

```powershell
src\DoctorDump.Agent\x64\Debug\DoctorDump.Agent.exe launch `
  --exe "samples\SampleCrashingApp\bin\Debug\net10.0\SampleCrashingApp.exe" `
  --args "--native-crash" `
  --type mini `
  --output "$env:LOCALAPPDATA\DumpDoctor\dumps"
```

5. Open the generated report from Dump History.

## Architecture

```text
DoctorDump.UI
  WPF desktop shell for process list, capture, monitoring, settings, and history

DoctorDump.Agent
  C++ Win32 CLI using Toolhelp, DebugActiveProcess, CreateProcess(DEBUG_ONLY_THIS_PROCESS), and MiniDumpWriteDump

DoctorDump.Analyzer
  .NET CLI that runs cdb.exe, !analyze -v, SOS !pe, SOS !clrstack -f, and parser self-tests

DoctorDump.Reporter
  .NET HTML report generator

DoctorDump.Core
  Shared contracts, settings, paths, and JSON defaults
```

## Project Layout

```text
src/
  DoctorDump.Agent/      C++ Win32 dump capture CLI
  DoctorDump.Analyzer/   cdb.exe / SOS analyzer
  DoctorDump.Core/       Shared .NET contracts, settings, and paths
  DoctorDump.Reporter/   HTML report generator
  DoctorDump.UI/         WPF desktop shell
samples/
  SampleCrashingApp/     Demo app with intentional crashes
docs/
  LLD.md                 Low-level design
```

## CLI Commands

```powershell
DoctorDump.Agent.exe list --json
DoctorDump.Agent.exe capture --pid 1234 --type mini --output "$env:LOCALAPPDATA\DumpDoctor\dumps"
DoctorDump.Agent.exe monitor --pid 1234 --type mini --output "$env:LOCALAPPDATA\DumpDoctor\dumps"
DoctorDump.Agent.exe launch --exe "C:\Apps\App.exe" --args "--crash" --type mini --output "$env:LOCALAPPDATA\DumpDoctor\dumps"
DoctorDump.Analyzer.exe --dump file.dmp --dump-id "<guid>" --output "<capture-folder>" --exception-code 0xE0434352
DoctorDump.Analyzer.exe --self-test
DoctorDump.Reporter.exe --metadata metadata.json --analysis analysis.json --output report.html
```

## Requirements

- Windows 10/11
- .NET 10 SDK
- Visual Studio C++ toolchain
- Windows Debugging Tools for `cdb.exe`

Install Debugging Tools through the Windows SDK installer with:

```text
OptionId.WindowsDesktopDebuggers
```

Expected debugger path:

```text
C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe
```

## Local Settings

DoctorDump stores settings at:

```text
%LOCALAPPDATA%\DumpDoctor\config\settings.json
```

The WPF settings row controls dump type, output folder, symbol path, automatic analysis, and retention days.

## Verification

```powershell
dotnet build DoctorDump.slnx
src\DoctorDump.Analyzer\bin\Debug\net10.0\DoctorDump.Analyzer.exe --self-test
```

The analyzer self-test validates exception-code parsing, managed exception parsing, faulting-thread parsing, and managed stack extraction.

## Portfolio Highlights

- C++/Win32 crash capture using `MiniDumpWriteDump`.
- Debugger attach and launch workflows through Windows Debug API.
- `cdb.exe` automation and raw debugger output preservation.
- SOS-based .NET exception and managed stack extraction.
- WPF product shell with local settings, history, reports, and re-analysis.
- Clean multi-project architecture with CLI boundaries.

## Roadmap

- Add launch argument support in the WPF UI.
- Improve native symbol UX for private PDB paths.
- Improve managed source-line resolution with private PDBs.
- Add formal unit tests around parser fixtures.
- Add screenshots/GIFs and a packaged release.
- Add optional Azure upload and team dashboard after the local MVP is stable.
