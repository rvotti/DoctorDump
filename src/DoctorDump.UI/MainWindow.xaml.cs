using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using DoctorDump.Core;
using DoctorDump.Core.Json;
using DoctorDump.Core.Models;

namespace DoctorDump.UI;

public partial class MainWindow : Window
{
    public ObservableCollection<ProcessSnapshot> Processes { get; } = [];
    public ObservableCollection<ProcessSnapshot> FilteredProcesses { get; } = [];
    public ObservableCollection<DumpMetadata> DumpHistory { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        RefreshProcesses();
        RefreshHistory();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshProcesses();

    private async void Capture_Click(object sender, RoutedEventArgs e) =>
        await RunAgentWorkflowAsync("capture", "Capturing dump", "Captured dump");

    private async void MonitorCrash_Click(object sender, RoutedEventArgs e) =>
        await RunAgentWorkflowAsync("monitor", "Monitoring for crash", "Captured crash dump");

    private async Task RunAgentWorkflowAsync(string command, string progressVerb, string successVerb)
    {
        if (ProcessGrid.SelectedItem is not ProcessSnapshot process)
        {
            StatusText.Text = "Select a process first.";
            return;
        }

        var outputRoot = DumpDoctorPaths.GetDatedDumpFolder(DateTimeOffset.UtcNow);
        Directory.CreateDirectory(outputRoot);

        var agentPath = FindAgentPath();
        if (!File.Exists(agentPath))
        {
            StatusText.Text = $"Agent not found yet: {agentPath}";
            return;
        }

        StatusText.Text = $"{progressVerb} for {process.Name} ({process.Pid})...";

        var startInfo = new ProcessStartInfo
        {
            FileName = agentPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add("--pid");
        startInfo.ArgumentList.Add(process.Pid.ToString());
        startInfo.ArgumentList.Add("--type");
        startInfo.ArgumentList.Add("mini");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputRoot);

        using var capture = Process.Start(startInfo);
        if (capture is null)
        {
            StatusText.Text = "Could not start agent.";
            return;
        }

        var output = await capture.StandardOutput.ReadToEndAsync();
        var error = await capture.StandardError.ReadToEndAsync();
        await capture.WaitForExitAsync();

        if (capture.ExitCode != 0)
        {
            StatusText.Text = $"{command} failed: {error.Trim()}";
            return;
        }

        var metadataPath = Directory.EnumerateFiles(outputRoot, "metadata.json", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (metadataPath is null)
        {
            StatusText.Text = $"{output.Trim()} Metadata was not created.";
            RefreshHistory();
            return;
        }

        await AnalyzeDumpAsync(metadataPath);
        var reportPath = await GenerateReportAsync(metadataPath);
        StatusText.Text = reportPath is null
            ? $"{output.Trim()} Report generation skipped."
            : $"{successVerb} and generated report: {reportPath}";

        RefreshHistory();
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not DumpMetadata metadata)
        {
            return;
        }

        var reportPath = Path.Combine(Path.GetDirectoryName(metadata.DumpFilePath) ?? string.Empty, "report.html");
        if (!File.Exists(reportPath))
        {
            StatusText.Text = $"Report not found: {reportPath}";
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = reportPath,
            UseShellExecute = true
        });
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyFilter();

    private void RefreshProcesses()
    {
        Processes.Clear();

        foreach (var process in Process.GetProcesses().OrderBy(p => p.ProcessName))
        {
            try
            {
                Processes.Add(new ProcessSnapshot(
                    process.Id,
                    process.ProcessName,
                    TryGetProcessPath(process),
                    Environment.Is64BitOperatingSystem ? "Unknown" : "x86"));
            }
            catch
            {
                // Some protected/system processes cannot be inspected from a normal user session.
            }
        }

        ApplyFilter();
        StatusText.Text = $"Loaded {Processes.Count} processes.";
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        FilteredProcesses.Clear();

        foreach (var process in Processes.Where(p =>
            string.IsNullOrWhiteSpace(query)
            || p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || p.Pid.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredProcesses.Add(process);
        }
    }

    private void RefreshHistory()
    {
        DumpHistory.Clear();

        if (!Directory.Exists(DumpDoctorPaths.DefaultDumpRoot))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(DumpDoctorPaths.DefaultDumpRoot, "metadata.json", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Take(25))
        {
            try
            {
                var json = File.ReadAllText(file);
                var item = System.Text.Json.JsonSerializer.Deserialize<DumpMetadata>(json, JsonDefaults.Options);
                if (item is not null)
                {
                    DumpHistory.Add(item);
                }
            }
            catch
            {
                // Ignore malformed metadata from experimental captures.
            }
        }
    }

    private static string? TryGetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string FindAgentPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "DoctorDump.Agent", "x64", "Debug", "DoctorDump.Agent.exe"));
    }

    private static string FindReporterPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "DoctorDump.Reporter", "bin", "Debug", "net10.0", "DoctorDump.Reporter.exe"));
    }

    private static string FindAnalyzerPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        return Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "..", "DoctorDump.Analyzer", "bin", "Debug", "net10.0", "DoctorDump.Analyzer.exe"));
    }

    private static async Task AnalyzeDumpAsync(string metadataPath)
    {
        var metadata = await ReadMetadataAsync(metadataPath);
        var folder = Path.GetDirectoryName(metadataPath)!;
        var analyzerPath = FindAnalyzerPath();

        if (!File.Exists(analyzerPath))
        {
            await WritePendingAnalysisAsync(
                metadata,
                folder,
                "Analyzer executable was not found. Build DoctorDump.Analyzer and re-run analysis.");
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = analyzerPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--dump");
        startInfo.ArgumentList.Add(metadata.DumpFilePath);
        startInfo.ArgumentList.Add("--dump-id");
        startInfo.ArgumentList.Add(metadata.DumpId.ToString());
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(folder);
        if (!string.IsNullOrWhiteSpace(metadata.ExceptionCode))
        {
            startInfo.ArgumentList.Add("--exception-code");
            startInfo.ArgumentList.Add(metadata.ExceptionCode);
        }

        using var analyzer = Process.Start(startInfo);
        if (analyzer is null)
        {
            await WritePendingAnalysisAsync(metadata, folder, "Could not start analyzer process.");
            return;
        }

        await analyzer.WaitForExitAsync();
        if (analyzer.ExitCode != 0 && !File.Exists(Path.Combine(folder, "analysis.json")))
        {
            await WritePendingAnalysisAsync(metadata, folder, "Analyzer failed before producing analysis.json.");
        }
    }

    private static async Task<string?> GenerateReportAsync(string metadataPath)
    {
        var metadata = await ReadMetadataAsync(metadataPath);
        var folder = Path.GetDirectoryName(metadataPath)!;
        var analysisPath = Path.Combine(folder, "analysis.json");
        var reportPath = Path.Combine(folder, "report.html");

        if (!File.Exists(analysisPath))
        {
            var analysis = new AnalysisResult
            {
                DumpId = metadata.DumpId,
                Status = "Pending",
                SymbolStatus = "NotRun",
                ProbableCause = "Initial report generated from capture metadata. Run the analyzer step to produce stack details."
            };

            await File.WriteAllTextAsync(analysisPath, JsonSerializer.Serialize(analysis, JsonDefaults.Options));
        }

        var reporterPath = FindReporterPath();
        if (!File.Exists(reporterPath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = reporterPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--metadata");
        startInfo.ArgumentList.Add(metadataPath);
        startInfo.ArgumentList.Add("--analysis");
        startInfo.ArgumentList.Add(analysisPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(reportPath);

        using var reporter = Process.Start(startInfo);
        if (reporter is null)
        {
            return null;
        }

        await reporter.WaitForExitAsync();
        return reporter.ExitCode == 0 ? reportPath : null;
    }

    private static async Task<DumpMetadata> ReadMetadataAsync(string metadataPath)
    {
        await using var stream = File.OpenRead(metadataPath);
        return await JsonSerializer.DeserializeAsync<DumpMetadata>(stream, JsonDefaults.Options)
            ?? throw new InvalidOperationException($"Could not read metadata from {metadataPath}.");
    }

    private static async Task WritePendingAnalysisAsync(DumpMetadata metadata, string folder, string reason)
    {
        var analysis = new AnalysisResult
        {
            DumpId = metadata.DumpId,
            Status = "Pending",
            SymbolStatus = "NotRun",
            ProbableCause = reason
        };

        await File.WriteAllTextAsync(
            Path.Combine(folder, "analysis.json"),
            JsonSerializer.Serialize(analysis, JsonDefaults.Options));
    }
}
