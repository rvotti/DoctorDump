# DoctorDump Project Guide

This document is the long-term memory page for DoctorDump. Read this before an interview, before recording a demo, or when reopening the project after a long break.

## One-Minute Pitch

DoctorDump is a local-first Windows crash diagnostics tool for native and .NET desktop applications. It captures process dumps, analyzes them with Windows Debugging Tools, extracts native and managed crash details, and generates an HTML report that a developer can use immediately.

The problem it solves is simple: many teams still receive vague crash reports like "the app closed suddenly" and then spend too much time manually attaching debuggers, finding dumps, running `cdb`, checking symbols, and explaining the stack trace. DoctorDump automates that workflow into a repeatable desktop tool.

## What Was Built

- A C++ Win32 agent that lists processes, captures dumps, attaches to running processes, and launches apps under crash monitoring.
- A WPF desktop UI for process selection, capture, crash monitoring, existing dump import, settings, history, report opening, and re-analysis.
- A .NET analyzer that automates `cdb.exe`, runs `!analyze -v`, loads SOS for .NET dumps, and preserves raw debugger output.
- A report generator that produces a readable HTML report from metadata and debugger analysis.
- A sample crashing app for demo and validation.
- A release packaging script that builds a ZIP with the UI, agent, analyzer, reporter, and sample app.

## Repository Map

```text
src/
  DoctorDump.Agent/       C++ Win32 CLI for process listing, dump capture, attach monitoring, launch monitoring
  DoctorDump.Analyzer/    .NET CLI around cdb.exe, !analyze -v, SOS !pe, SOS !clrstack -f
  DoctorDump.Core/        Shared models, paths, settings, JSON options
  DoctorDump.Reporter/    .NET HTML report generator
  DoctorDump.UI/          WPF desktop application

samples/
  SampleCrashingApp/      Demo app that can intentionally native-crash or managed-crash

tests/
  DoctorDump.Analyzer.Tests/  Lightweight parser verification runner

docs/
  LLD.md                  Low-level design
  DEMO.md                 Screenshot/GIF recording checklist
  PROJECT_GUIDE.md        This document

scripts/
  package-release.ps1     Release ZIP builder
```

## End-To-End Flow

1. The user launches `DoctorDump.UI`.
2. The UI shows processes using normal .NET process APIs.
3. For manual capture or crash monitoring, the UI starts `DoctorDump.Agent.exe`.
4. The native agent uses Windows APIs to inspect or debug the target process.
5. The agent writes a `.dmp` file and `metadata.json`.
6. The UI starts `DoctorDump.Analyzer.exe`.
7. The analyzer runs `cdb.exe` against the dump, saves `raw-debugger-output.txt`, and emits `analysis.json`.
8. The UI starts `DoctorDump.Reporter.exe`.
9. The reporter creates `report.html`.
10. The UI refreshes history and lets the user open the report or folder.

## Important Files Produced Per Capture

```text
%LOCALAPPDATA%\DumpDoctor\dumps\yyyy-MM-dd\{dumpId}\
  {dumpId}.dmp
  metadata.json
  raw-debugger-output.txt
  analysis.json
  report.html
```

- `.dmp`: crash/process dump.
- `metadata.json`: capture context such as process name, PID, dump path, exception code, machine, OS.
- `raw-debugger-output.txt`: exact `cdb.exe` output for transparency and manual follow-up.
- `analysis.json`: structured summary parsed from debugger output.
- `report.html`: human-readable developer report.

## Main Technical Decisions

### Why C++ For The Agent?

Dump capture and debug-event monitoring are native Windows tasks. C++ gives direct access to `MiniDumpWriteDump`, `DebugActiveProcess`, `CreateProcess(DEBUG_ONLY_THIS_PROCESS)`, Toolhelp APIs, process handles, and exception debug events. This also highlights relevant C++/Win32 skills.

### Why .NET For Analyzer/Reporter/UI?

The analyzer and reporter are orchestration-heavy and text/JSON-heavy. .NET is productive for process execution, parsing, JSON serialization, file I/O, and WPF UI. Keeping the native part small reduces risk while still using C++ where it matters.

### Why Shell Out To `cdb.exe` Instead Of DbgEng COM?

For MVP, `cdb.exe` is easier to automate and verify. It gives the same debugger output a Windows developer already understands. The raw output is preserved, so the tool is transparent. A future version can replace or augment this with DbgEng COM for richer structured data.

### Why Preserve Raw Debugger Output?

Parser logic will never cover every debugger variant. Saving raw output lets a senior engineer inspect the full truth when structured parsing misses something. It also makes bug reports and parser improvements easier.

### Why Local-First?

Crash dumps may contain memory, paths, arguments, usernames, connection strings, or private data. Local-first avoids privacy and compliance concerns. Cloud upload can be added later as an explicit opt-in.

## Key Windows APIs To Remember

- `CreateToolhelp32Snapshot`, `Process32FirstW`, `Process32NextW`: enumerate processes.
- `OpenProcess`: open target process with required permissions.
- `QueryFullProcessImageNameW`: resolve executable path.
- `IsWow64Process2`: detect process architecture where possible.
- `MiniDumpWriteDump`: create the dump.
- `DebugActiveProcess`: attach to an already-running process and receive debug events.
- `CreateProcessW` with `DEBUG_ONLY_THIS_PROCESS`: launch and monitor a new process.
- `WaitForDebugEvent`, `ContinueDebugEvent`: debug loop for crash monitoring.

## Analyzer Commands

The analyzer creates a temporary command file for `cdb.exe`:

```text
.sympath {symbolPath}
.reload
!analyze -v
.loadby sos coreclr
!pe
!clrstack -f
~* k
lm
q
```

These commands give:

- Native exception and probable cause from `!analyze -v`.
- Managed exception details from SOS `!pe`.
- Managed call stack from SOS `!clrstack -f`.
- Native stacks from `~* k`.
- Loaded module information from `lm`.

## How To Test From Fresh Clone

### 1. Prerequisites

- Windows 10/11.
- .NET 10 SDK.
- Visual Studio C++ toolchain.
- Windows Debugging Tools for `cdb.exe`.

Expected `cdb.exe` path:

```text
C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe
```

### 2. Build Everything

```powershell
dotnet build DoctorDump.slnx -m:1
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" src\DoctorDump.Agent\DoctorDump.Agent.vcxproj /p:Configuration=Debug /p:Platform=x64
```

Use `-m:1` if the machine is memory-constrained or if parallel builds cause file locking.

### 3. Run Automated Parser Check

```powershell
dotnet run --project tests\DoctorDump.Analyzer.Tests\DoctorDump.Analyzer.Tests.csproj
```

Expected output:

```text
Analyzer parser self-test passed.
```

### 4. Run Agent Smoke Test

```powershell
src\DoctorDump.Agent\x64\Debug\DoctorDump.Agent.exe list --json
```

Expected result: JSON array of running processes.

### 5. Run UI Demo

```powershell
dotnet run --project src\DoctorDump.UI\DoctorDump.UI.csproj
```

In the UI:

1. Put `--native-crash` in **Launch Args**.
2. Click **Launch App**.
3. Select:

```text
samples\SampleCrashingApp\bin\Debug\net10.0\SampleCrashingApp.exe
```

4. Wait for capture/analyze/report.
5. Open the report from Dump History.

Goal confirmation:

- A new dump folder appears under `%LOCALAPPDATA%\DumpDoctor\dumps`.
- `metadata.json` exists.
- `{dumpId}.dmp` exists.
- `raw-debugger-output.txt` exists if analyzer ran.
- `analysis.json` exists.
- `report.html` exists and opens from the UI.

### 6. Build Release ZIP

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.1.0
```

Expected output:

```text
artifacts\release\DoctorDump-0.1.0-win-x64.zip
```

Packaged smoke checks:

```powershell
artifacts\publish\DoctorDump-0.1.0-win-x64\tools\Analyzer\DoctorDump.Analyzer.exe --self-test
artifacts\publish\DoctorDump-0.1.0-win-x64\tools\DoctorDump.Agent.exe list --json
```

## Recruiter Explanation

### Short Answer

I built DoctorDump to automate Windows crash diagnostics for desktop applications. It captures crash dumps using a native C++ agent, analyzes them with Windows Debugging Tools and SOS, and generates developer-friendly HTML reports from a WPF UI. It demonstrates C++, Win32 debugging APIs, .NET, WPF, process automation, JSON contracts, and release packaging.

### Slightly Longer Answer

The tool solves a real pain point: when a desktop app crashes, teams often need a developer to manually reproduce the issue, capture a dump, run debugger commands, inspect symbols, and explain the stack. DoctorDump turns that into a repeatable workflow. A user can select or launch an app, capture the crash, and get a report containing metadata, exception details, native/managed stacks, recommendations, and raw debugger output.

### Why It Is Relevant For C++ Roles

The core capture layer is C++/Win32. It uses Windows process enumeration, debug attach/launch flows, and `MiniDumpWriteDump`. These are practical systems-level skills used in native desktop, MFC, Win32, performance, production support, and diagnostics work.

### Why It Is Relevant For .NET/Azure/Product Roles

The orchestration layer is .NET and WPF. It shows ability to build a usable desktop product, integrate native tools, manage local settings/history, serialize contracts, automate command-line tools, and package a release. The local MVP can later become an Azure-backed team dashboard.

## Common Interview Questions

### Why not just use Windows Error Reporting?

Windows Error Reporting is useful, but it is not always configured for small internal teams, does not provide the same local developer workflow, and may not give immediate custom reports. DoctorDump is a developer-facing local workflow that can work before a team has crash infrastructure.

### Why not just use ProcDump?

ProcDump is excellent. DoctorDump is not trying to replace it completely. The value here is an integrated workflow: UI, capture, monitor, launch, analysis, report generation, local history, managed SOS parsing, and portfolio-level extensibility. ProcDump could even become a future capture backend option.

### How do you avoid exposing sensitive dump data?

The MVP is local-first. Dumps and reports stay under `%LOCALAPPDATA%\DumpDoctor` unless the user explicitly shares them. A future cloud feature should include opt-in upload, retention controls, redaction, and access control.

### What happens if `cdb.exe` is missing?

The analyzer creates a `DebuggerNotFound` analysis result instead of crashing the workflow. The UI/report path can still complete with a clear message telling the user to install Debugging Tools for Windows.

### What happens if symbols are missing?

The analyzer still preserves raw debugger output and marks symbol quality as partial/unknown where possible. The README and UI settings support configuring private PDB paths plus the Microsoft public symbol server.

### Why parse text output instead of structured debugging APIs?

Text parsing is acceptable for an MVP because it is fast to build, transparent, and easy to validate against real `cdb` output. The raw output is always saved. For a production-grade version, I would add DbgEng COM or another structured API layer and keep the text parser as a fallback.

### What are the hardest parts?

Crash monitoring with debug events, producing a dump while the process is stopped at the exception, making analyzer execution resilient, and balancing MVP parser coverage with raw-output transparency.

### How would you scale this into a product?

I would add a signed installer, richer parser fixtures, symbol/source indexing, dump redaction, optional Azure Blob upload, team dashboards, issue grouping by stack signature, retention policies, and integrations with GitHub/Azure DevOps/Jira.

### What would you improve next?

The next practical improvements are screenshots/GIFs, GitHub Release upload, more parser fixture tests, richer source-line extraction, and optionally an MSIX installer. Product-wise, the highest-value next feature is crash grouping by signature.

## Troubleshooting

### UI Says Agent Not Found

Build the native agent:

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" src\DoctorDump.Agent\DoctorDump.Agent.vcxproj /p:Configuration=Debug /p:Platform=x64
```

For packaged builds, confirm:

```text
tools\DoctorDump.Agent.exe
```

exists beside the published UI.

### Analyzer Does Not Produce Real Stack Details

Check that `cdb.exe` exists and symbols are configured:

```text
C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe
srv*C:\Symbols*https://msdl.microsoft.com/download/symbols
```

For private app frames, include your build/PDB folder before the Microsoft symbol server.

### Build Fails With File Lock Or Out Of Memory

Run sequentially:

```powershell
dotnet build DoctorDump.slnx -m:1
```

Close running instances of `DoctorDump.UI`, `DoctorDump.Analyzer.Tests`, or any console still executing from `bin` or `obj`.

### PowerShell Script Execution Is Blocked

Run the packaging script with process-scoped bypass:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.1.0
```

## Definition Of Done

The MVP goal is complete when all of these pass:

- `dotnet build DoctorDump.slnx -m:1` succeeds.
- Native agent MSBuild succeeds.
- Analyzer parser test prints `Analyzer parser self-test passed.`
- Agent `list --json` returns process JSON.
- UI can launch `SampleCrashingApp.exe` with `--native-crash`.
- A dump folder is created with `.dmp`, `metadata.json`, `analysis.json`, and `report.html`.
- Release ZIP script creates `artifacts\release\DoctorDump-0.1.0-win-x64.zip`.

## Pending Items To Revisit

- Add actual screenshots/GIFs under `docs/media`.
- Create a GitHub Release and upload the ZIP.
- Add installer/MSIX packaging.
- Add deeper parser fixtures for native, managed, symbol-missing, and debugger-missing cases.
- Improve source-line extraction when private PDB resolution appears in debugger output.
- Consider Azure upload, team dashboard, crash grouping, and trend analytics.
