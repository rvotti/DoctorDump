# DumpDoctor Low-Level Design

DumpDoctor is a local-first Windows diagnostic tool for capturing process dumps, analyzing them, and generating developer-friendly reports.

## MVP Modules

- `DoctorDump.Agent`: native C++ command-line tool for process enumeration and dump capture.
- `DoctorDump.Analyzer`: .NET console tool that runs `cdb.exe`, saves raw debugger output, and emits `analysis.json`.
- `DoctorDump.Core`: shared .NET models, paths, and serialization helpers.
- `DoctorDump.Reporter`: .NET console tool that turns metadata and analysis JSON into HTML/Markdown reports.
- `DoctorDump.UI`: WPF desktop shell for selecting processes, capturing dumps, and viewing history.
- `SampleCrashingApp`: intentionally faulty app used to demonstrate capture and reporting.

## Data Contracts

### Dump Metadata

```json
{
  "dumpId": "guid",
  "processName": "SampleCrashingApp.exe",
  "pid": 1234,
  "processPath": "C:\\Apps\\SampleCrashingApp.exe",
  "architecture": "x64",
  "capturedAtUtc": "2026-05-29T08:30:00Z",
  "captureReason": "Manual",
  "exceptionCode": null,
  "dumpType": "MiniDump",
  "dumpFilePath": "C:\\Users\\me\\AppData\\Local\\DumpDoctor\\dumps\\guid.dmp",
  "machineName": "DEVBOX",
  "osVersion": "Windows 11",
  "dumpDoctorVersion": "0.1.0"
}
```

### Analysis Result

```json
{
  "dumpId": "guid",
  "status": "Completed",
  "exceptionCode": "0xC0000005",
  "exceptionDescription": "Access violation",
  "faultingModule": "BillingCore.dll",
  "faultingThreadId": 4212,
  "symbolStatus": "Partial",
  "probableCause": "Invalid memory access or null pointer dereference",
  "callStack": []
}
```

## Local Storage

Root folder:

```text
%LOCALAPPDATA%\DumpDoctor
```

Structure:

```text
dumps\
  yyyy-MM-dd\
    {dumpId}.dmp
    metadata.json
    analysis.json
    report.html
config\
  settings.json
logs\
  dumpdoctor.log
```

## CLI Contracts

```text
DoctorDump.Agent.exe list --json
DoctorDump.Agent.exe capture --pid 1234 --type mini --output "%LOCALAPPDATA%\DumpDoctor\dumps"
DoctorDump.Analyzer.exe --dump dump.dmp --dump-id {dumpId} --output {capture-folder}
DoctorDump.Reporter.exe --metadata metadata.json --analysis analysis.json --output report.html
```

## Capture Flow

1. UI requests process list.
2. Agent returns process metadata as JSON.
3. User selects a process and clicks capture.
4. UI invokes Agent with `capture`.
5. Agent opens the target process with dump permissions.
6. Agent calls `MiniDumpWriteDump`.
7. Agent writes dump and metadata JSON.
8. Analyzer runs `cdb.exe` and writes `raw-debugger-output.txt` plus `analysis.json`.
9. Reporter generates an HTML report.
10. UI refreshes dump history.

## Agent Design

The native agent owns Windows-specific diagnostic work.

Responsibilities:

- Enumerate processes.
- Resolve process image path.
- Detect architecture where possible.
- Capture minidumps using `MiniDumpWriteDump`.
- Return machine-readable JSON to callers.

Primary Win32 APIs:

- `CreateToolhelp32Snapshot`
- `Process32FirstW`
- `Process32NextW`
- `OpenProcess`
- `QueryFullProcessImageNameW`
- `IsWow64Process2`
- `MiniDumpWriteDump`

## UI Design

The WPF UI is intentionally simple for MVP:

- Process list with search and refresh.
- Capture button.
- Dump history list.
- Report preview/open action.
- Settings page for output folder and symbol path.

## Analyzer Design

Analyzer Phase 1 shells out to `cdb.exe` because it is fast to integrate and easy to validate manually. It runs:

```text
.sympath {symbolPath}
.reload
!analyze -v
~* k
lm
q
```

Outputs:

- `raw-debugger-output.txt`
- `analysis.json`

If `cdb.exe` is not installed, the analyzer writes a `DebuggerNotFound` result so the UI/report flow still completes.

Future analyzer improvements:

- Improve regex parsing of `!analyze -v` variants.
- Add `DbgEng` COM integration for structured analysis.
- Add SOS support for managed .NET dumps.
