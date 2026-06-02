# DoctorDump Demo Script

Use this flow to record portfolio screenshots or a short GIF.

## Setup

```powershell
dotnet build DoctorDump.slnx
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe" src\DoctorDump.Agent\DoctorDump.Agent.vcxproj /p:Configuration=Debug /p:Platform=x64
dotnet run --project src\DoctorDump.UI\DoctorDump.UI.csproj
```

## Capture Sequence

1. Show the main DoctorDump window with process list, settings, and dump history.
2. Type `--native-crash` in **Launch Args**.
3. Click **Launch App** and choose:

```text
samples\SampleCrashingApp\bin\Debug\net10.0\SampleCrashingApp.exe
```

4. Wait for the crash capture and report generation to finish.
5. In **Dump History**, click **Open Report**.
6. Capture the generated report sections:
   - summary
   - native stack
   - managed stack, if using `--managed-crash`
   - recommendations
   - raw debugger output link/section

## Suggested README Assets

Store final assets here:

```text
docs\media\doctordump-ui.png
docs\media\doctordump-report.png
docs\media\doctordump-demo.gif
```

Keep screenshots focused on the actual UI and report. Avoid showing unrelated desktop windows, personal paths, or private process names.
