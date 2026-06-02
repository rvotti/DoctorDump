using System.Net;
using System.Text;
using System.Text.Json;
using DoctorDump.Core.Json;
using DoctorDump.Core.Models;

var options = CliOptions.Parse(args);

if (options is null)
{
    PrintUsage();
    return 2;
}

var metadata = await ReadJsonAsync<DumpMetadata>(options.MetadataPath);
var analysis = await ReadAnalysisOrPendingAsync(options.AnalysisPath, metadata.DumpId);
var rawDebuggerOutputPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.AnalysisPath))!, "raw-debugger-output.txt");
var rawDebuggerOutput = File.Exists(rawDebuggerOutputPath)
    ? await File.ReadAllTextAsync(rawDebuggerOutputPath)
    : null;

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath))!);
await File.WriteAllTextAsync(options.OutputPath, RenderHtml(metadata, analysis, rawDebuggerOutput), Encoding.UTF8);
Console.WriteLine(options.OutputPath);
return 0;

static async Task<T> ReadJsonAsync<T>(string path)
{
    await using var stream = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<T>(stream, JsonDefaults.Options)
        ?? throw new InvalidOperationException($"Could not read JSON from {path}.");
}

static AnalysisResult CreatePendingAnalysis(Guid dumpId) => new()
{
    DumpId = dumpId,
    Status = "Pending",
    ProbableCause = "Analysis has not run yet. Capture metadata is available."
};

static async Task<AnalysisResult> ReadAnalysisOrPendingAsync(string path, Guid dumpId)
{
    if (!File.Exists(path))
    {
        return CreatePendingAnalysis(dumpId);
    }

    try
    {
        return await ReadJsonAsync<AnalysisResult>(path);
    }
    catch (JsonException)
    {
        return CreatePendingAnalysis(dumpId);
    }
}

static string RenderHtml(DumpMetadata metadata, AnalysisResult analysis, string? rawDebuggerOutput)
{
    var recommendation = BuildRecommendation(metadata, analysis);
    var stackRows = RenderStackRows(analysis.CallStack);
    var managedStackRows = RenderStackRows(analysis.ManagedCallStack);

    return $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>DoctorDump Report - {{E(metadata.ProcessName)}}</title>
          <style>
            body { font-family: Segoe UI, Arial, sans-serif; margin: 32px; color: #1f2937; }
            h1, h2 { margin-bottom: 8px; }
            .summary { display: grid; grid-template-columns: repeat(2, minmax(220px, 1fr)); gap: 12px; margin: 20px 0; }
            .item { border: 1px solid #d1d5db; border-radius: 6px; padding: 12px; }
            .label { color: #6b7280; font-size: 12px; text-transform: uppercase; }
            .value { font-size: 15px; margin-top: 4px; word-break: break-word; }
            .callout { border-left: 4px solid #2563eb; background: #eff6ff; padding: 12px 14px; border-radius: 4px; }
            table { width: 100%; border-collapse: collapse; margin-top: 12px; }
            th, td { border: 1px solid #d1d5db; padding: 8px; text-align: left; }
            th { background: #f3f4f6; }
            code { background: #f3f4f6; padding: 2px 4px; border-radius: 4px; }
            pre { background: #111827; color: #f9fafb; padding: 14px; border-radius: 6px; overflow: auto; max-height: 520px; }
            summary { cursor: pointer; font-weight: 600; margin-top: 20px; }
          </style>
        </head>
        <body>
          <h1>DoctorDump Report</h1>
          <p>{{E(metadata.ProcessName)}} captured at {{metadata.CapturedAtUtc:yyyy-MM-dd HH:mm:ss}} UTC.</p>

          <section class="summary">
            {{Item("Process", metadata.ProcessName)}}
            {{Item("PID", metadata.Pid.ToString())}}
            {{Item("Reason", metadata.CaptureReason)}}
            {{Item("Dump Type", metadata.DumpType)}}
            {{Item("Metadata Exception", metadata.ExceptionCode ?? "Not available")}}
            {{Item("Analyzer Exception", analysis.ExceptionCode ?? "Not available")}}
            {{Item("Managed Exception", analysis.ManagedExceptionType ?? "Not available")}}
            {{Item("Faulting Module", analysis.FaultingModule ?? "Not available")}}
            {{Item("Symbol Status", analysis.SymbolStatus)}}
            {{Item("Status", analysis.Status)}}
          </section>

          <h2>Probable Cause</h2>
          <p>{{E(analysis.ProbableCause ?? "No probable cause generated yet.")}}</p>

          <h2>Recommended Next Action</h2>
          <p class="callout">{{E(recommendation)}}</p>

          <h2>Dump File</h2>
          <p><code>{{E(metadata.DumpFilePath)}}</code></p>

          {{ManagedSection(analysis, managedStackRows)}}

          <h2>Faulting Thread Call Stack</h2>
          <table>
            <thead><tr><th>#</th><th>Module</th><th>Function</th><th>Source</th><th>Line</th></tr></thead>
            <tbody>{{stackRows}}</tbody>
          </table>

          <h2>System</h2>
          <p>{{E(metadata.MachineName)}} | {{E(metadata.OsVersion)}} | {{E(metadata.Architecture)}} | DoctorDump {{E(metadata.DoctorDumpVersion)}}</p>

          {{RawOutputSection(rawDebuggerOutput)}}
        </body>
        </html>
        """;
}

static string Item(string label, string value) =>
    $"""<div class="item"><div class="label">{E(label)}</div><div class="value">{E(value)}</div></div>""";

static string RenderStackRows(IReadOnlyList<StackFrameInfo> frames) =>
    frames.Count == 0
        ? "<tr><td colspan=\"5\">No stack frames available yet.</td></tr>"
        : string.Join(Environment.NewLine, frames.Select(frame =>
            $"<tr><td>{frame.Index}</td><td>{E(frame.Module)}</td><td>{E(frame.Function)}</td><td>{E(frame.Source)}</td><td>{frame.Line}</td></tr>"));

static string ManagedSection(AnalysisResult analysis, string managedStackRows)
{
    if (string.IsNullOrWhiteSpace(analysis.ManagedExceptionType) && analysis.ManagedCallStack.Count == 0)
    {
        return string.Empty;
    }

    return $$"""
          <h2>Managed Exception</h2>
          <section class="summary">
            {{Item("Type", analysis.ManagedExceptionType ?? "Not available")}}
            {{Item("Message", analysis.ManagedExceptionMessage ?? "Not available")}}
          </section>

          <h2>Managed Call Stack</h2>
          <table>
            <thead><tr><th>#</th><th>Module</th><th>Function</th><th>Source</th><th>Line</th></tr></thead>
            <tbody>{{managedStackRows}}</tbody>
          </table>
        """;
}

static string RawOutputSection(string? rawDebuggerOutput) =>
    string.IsNullOrWhiteSpace(rawDebuggerOutput)
        ? string.Empty
        : $"""<details><summary>Raw Debugger Output</summary><pre>{E(rawDebuggerOutput)}</pre></details>""";

static string BuildRecommendation(DumpMetadata metadata, AnalysisResult analysis)
{
    var code = analysis.ExceptionCode ?? metadata.ExceptionCode;

    if (string.Equals(code, "0xC0000005", StringComparison.OrdinalIgnoreCase))
    {
        return "Investigate invalid memory access around the top stack frames. Check null pointers, object lifetime, and buffer boundaries.";
    }

    if (string.Equals(code, "0xE0434352", StringComparison.OrdinalIgnoreCase))
    {
        return string.IsNullOrWhiteSpace(analysis.ManagedExceptionType)
            ? "This is a .NET CLR exception. Re-run with SOS available or use application logs to identify the managed exception type and source line."
            : $"Investigate {analysis.ManagedExceptionType} around the top managed frames. Use private PDBs/source to map methods to exact source lines.";
    }

    if (analysis.SymbolStatus is "Partial" or "Unknown")
    {
        return "Improve symbol loading first. Configure local PDB paths and Microsoft public symbols, then re-run analysis.";
    }

    if (metadata.CaptureReason == "Imported")
    {
        return "Imported dump analysis is complete. Compare the top stack frames with source history and re-run with symbols if frames are incomplete.";
    }

    return "Review the top stack frames and raw debugger output, then re-run analysis after adding private symbols for the target application.";
}

static string E(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

static void PrintUsage()
{
    Console.WriteLine("Usage: DoctorDump.Reporter --metadata metadata.json --analysis analysis.json --output report.html");
}

internal sealed record CliOptions(string MetadataPath, string AnalysisPath, string OutputPath)
{
    public static CliOptions? Parse(string[] args)
    {
        string? metadata = null;
        string? analysis = null;
        string? output = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--metadata" && i + 1 < args.Length) metadata = args[++i];
            else if (args[i] == "--analysis" && i + 1 < args.Length) analysis = args[++i];
            else if (args[i] == "--output" && i + 1 < args.Length) output = args[++i];
        }

        return metadata is null || analysis is null || output is null
            ? null
            : new CliOptions(metadata, analysis, output);
    }
}
