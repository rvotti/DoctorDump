param(
    [string]$Version = "0.1.0",
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$MSBuildPath = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\amd64\MSBuild.exe"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifactsRoot = Join-Path $repoRoot "artifacts"
$publishRoot = Join-Path $artifactsRoot "publish"
$releaseRoot = Join-Path $artifactsRoot "release"
$packageName = "DoctorDump-$Version-win-x64"
$packageRoot = Join-Path $publishRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"

function Assert-UnderRepo([string]$PathToCheck) {
    $fullPath = [System.IO.Path]::GetFullPath($PathToCheck)
    $fullRepo = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $fullPath.StartsWith($fullRepo, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove or write outside repository: $fullPath"
    }
}

Assert-UnderRepo $packageRoot
Assert-UnderRepo $zipPath

if (-not (Test-Path $MSBuildPath)) {
    throw "MSBuild was not found: $MSBuildPath"
}

if (Test-Path $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

dotnet build (Join-Path $repoRoot "DoctorDump.slnx") -c $Configuration

& $MSBuildPath `
    (Join-Path $repoRoot "src\DoctorDump.Agent\DoctorDump.Agent.vcxproj") `
    "/p:Configuration=$Configuration" `
    "/p:Platform=$Platform"

dotnet publish (Join-Path $repoRoot "src\DoctorDump.UI\DoctorDump.UI.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $packageRoot

$toolsRoot = Join-Path $packageRoot "tools"
$analyzerRoot = Join-Path $toolsRoot "Analyzer"
$reporterRoot = Join-Path $toolsRoot "Reporter"
$samplesRoot = Join-Path $packageRoot "samples"

dotnet publish (Join-Path $repoRoot "src\DoctorDump.Analyzer\DoctorDump.Analyzer.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $analyzerRoot

dotnet publish (Join-Path $repoRoot "src\DoctorDump.Reporter\DoctorDump.Reporter.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $reporterRoot

dotnet publish (Join-Path $repoRoot "samples\SampleCrashingApp\SampleCrashingApp.csproj") `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o (Join-Path $samplesRoot "SampleCrashingApp")

Copy-Item `
    -LiteralPath (Join-Path $repoRoot "src\DoctorDump.Agent\$Platform\$Configuration\DoctorDump.Agent.exe") `
    -Destination (Join-Path $toolsRoot "DoctorDump.Agent.exe") `
    -Force

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\LLD.md") -Destination $packageRoot -Force

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force

Write-Host "Release package created:"
Write-Host $zipPath
