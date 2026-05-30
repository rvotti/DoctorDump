using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DoctorDump.Core.Json;
using DoctorDump.Core.Models;

var options = CliOptions.Parse(args);
if (options is null)
{
    PrintUsage();
    return 2;
}

Directory.CreateDirectory(options.OutputDirectory);

var rawOutputPath = Path.Combine(options.OutputDirectory, "raw-debugger-output.txt");
var analysisPath = Path.Combine(options.OutputDirectory, "analysis.json");

var cdbPath = DebuggerLocator.FindCdb(options.CdbPath);
AnalysisResult analysis;

if (cdbPath is null)
{
    analysis = CreateToolMissingResult(options.DumpId);
    await File.WriteAllTextAsync(rawOutputPath, "cdb.exe was not found. Install Debugging Tools for Windows from the Windows SDK.", options.CancellationToken);
}
else
{
    var rawOutput = await CdbRunner.RunAsync(cdbPath, options.DumpPath, options.SymbolPath, options.CancellationToken);
    await File.WriteAllTextAsync(rawOutputPath, rawOutput, options.CancellationToken);
    analysis = CdbOutputParser.Parse(options.DumpId, rawOutput, options.ExceptionCode);
}

await File.WriteAllTextAsync(analysisPath, JsonSerializer.Serialize(analysis, JsonDefaults.Options), options.CancellationToken);
Console.WriteLine(analysisPath);
return analysis.Status == "Completed" || analysis.Status == "DebuggerNotFound" ? 0 : 1;

static AnalysisResult CreateToolMissingResult(Guid dumpId) => new()
{
    DumpId = dumpId,
    Status = "DebuggerNotFound",
    SymbolStatus = "NotRun",
    ProbableCause = "cdb.exe was not found. Install Debugging Tools for Windows, then re-run analysis."
};

static void PrintUsage()
{
    Console.WriteLine("Usage: DoctorDump.Analyzer --dump file.dmp --dump-id guid --output folder [--symbols symbol-path] [--cdb path]");
}

internal sealed record CliOptions(
    string DumpPath,
    Guid DumpId,
    string OutputDirectory,
    string SymbolPath,
    string? CdbPath,
    string? ExceptionCode,
    CancellationToken CancellationToken)
{
    public static CliOptions? Parse(string[] args)
    {
        string? dumpPath = null;
        string? dumpId = null;
        string? output = null;
        string? symbols = null;
        string? cdbPath = null;
        string? exceptionCode = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--dump" && i + 1 < args.Length) dumpPath = args[++i];
            else if (args[i] == "--dump-id" && i + 1 < args.Length) dumpId = args[++i];
            else if (args[i] == "--output" && i + 1 < args.Length) output = args[++i];
            else if (args[i] == "--symbols" && i + 1 < args.Length) symbols = args[++i];
            else if (args[i] == "--cdb" && i + 1 < args.Length) cdbPath = args[++i];
            else if (args[i] == "--exception-code" && i + 1 < args.Length) exceptionCode = args[++i];
        }

        if (dumpPath is null || !File.Exists(dumpPath) || dumpId is null || !Guid.TryParse(dumpId, out var id) || output is null)
        {
            return null;
        }

        return new CliOptions(
            dumpPath,
            id,
            output,
            string.IsNullOrWhiteSpace(symbols) ? @"srv*C:\Symbols*https://msdl.microsoft.com/download/symbols" : symbols,
            cdbPath,
            exceptionCode,
            CancellationToken.None);
    }
}

internal static class DebuggerLocator
{
    public static string? FindCdb(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath) && File.Exists(explicitPath))
        {
            return explicitPath;
        }

        var pathMatch = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, "cdb.exe"))
            .FirstOrDefault(File.Exists);

        if (pathMatch is not null)
        {
            return pathMatch;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Debuggers", "x64", "cdb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Debuggers", "x86", "cdb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Kits", "10", "Debuggers", "x64", "cdb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Kits", "10", "Debuggers", "x86", "cdb.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}

internal static class CdbRunner
{
    public static async Task<string> RunAsync(string cdbPath, string dumpPath, string symbolPath, CancellationToken cancellationToken)
    {
        var commandFile = Path.Combine(Path.GetTempPath(), $"doctordump-cdb-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(commandFile, new[]
        {
            $".sympath {symbolPath}",
            ".reload",
            "!analyze -v",
            "~* k",
            "lm",
            "q"
        }, cancellationToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = cdbPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-z");
        startInfo.ArgumentList.Add(dumpPath);
        startInfo.ArgumentList.Add("-cf");
        startInfo.ArgumentList.Add(commandFile);

        try
        {
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start cdb.exe.");
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return stdout + Environment.NewLine + stderr;
        }
        finally
        {
            try
            {
                File.Delete(commandFile);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }
}

internal static partial class CdbOutputParser
{
    public static AnalysisResult Parse(Guid dumpId, string rawOutput, string? preferredExceptionCode)
    {
        var exceptionCode = NormalizeExceptionCode(preferredExceptionCode) ?? NormalizeExceptionCode(
            FirstMatch(rawOutput, ExceptionCodeRegex())
            ?? FirstMatch(rawOutput, ExceptionCodeStrRegex()));
        var exceptionDescription = DescribeException(exceptionCode);
        var faultingModule = CleanModuleName(FirstMatch(rawOutput, FaultingModuleRegex()))
            ?? FirstMatch(rawOutput, ImageNameRegex())
            ?? FirstMatch(rawOutput, ModuleNameRegex());
        var probableCause = FirstMatch(rawOutput, ProbablyCausedByRegex());
        var stack = ParseStack(rawOutput);

        return new AnalysisResult
        {
            DumpId = dumpId,
            Status = "Completed",
            ExceptionCode = exceptionCode,
            ExceptionDescription = exceptionDescription,
            FaultingModule = faultingModule,
            FaultingThreadId = ParseFaultingThread(rawOutput),
            SymbolStatus = DetermineSymbolStatus(rawOutput),
            ProbableCause = BuildProbableCause(exceptionCode, exceptionDescription, probableCause, faultingModule),
            CallStack = stack
        };
    }

    private static IReadOnlyList<StackFrameInfo> ParseStack(string rawOutput)
    {
        var frames = new List<StackFrameInfo>();

        foreach (Match match in StackFrameRegex().Matches(rawOutput))
        {
            var symbol = match.Groups["symbol"].Value.Trim();
            var module = symbol.Contains('!') ? symbol.Split('!', 2)[0] : null;
            var function = symbol.Contains('!') ? symbol.Split('!', 2)[1] : symbol;

            frames.Add(new StackFrameInfo(
                frames.Count,
                string.IsNullOrWhiteSpace(module) ? null : module,
                string.IsNullOrWhiteSpace(function) ? null : function,
                null,
                null));

            if (frames.Count >= 40)
            {
                break;
            }
        }

        return frames;
    }

    private static int? ParseFaultingThread(string rawOutput)
    {
        var value = FirstMatch(rawOutput, FaultingThreadRegex());
        if (value is null)
        {
            return null;
        }

        return int.TryParse(value, System.Globalization.NumberStyles.HexNumber, null, out var threadId)
            || int.TryParse(value, out threadId)
                ? threadId
                : null;
    }

    private static string DetermineSymbolStatus(string rawOutput)
    {
        if (rawOutput.Contains("ERROR: Symbol file could not be found", StringComparison.OrdinalIgnoreCase)
            || rawOutput.Contains("Unable to load image", StringComparison.OrdinalIgnoreCase))
        {
            return "Partial";
        }

        if (rawOutput.Contains("symbols loaded", StringComparison.OrdinalIgnoreCase)
            || rawOutput.Contains("public symbols", StringComparison.OrdinalIgnoreCase))
        {
            return "Loaded";
        }

        return "Unknown";
    }

    private static string BuildProbableCause(string? code, string? description, string? causedBy, string? module)
    {
        if (!string.IsNullOrWhiteSpace(causedBy))
        {
            return $"Debugger reported probable cause: {causedBy}.";
        }

        if (string.Equals(code, "0xC0000005", StringComparison.OrdinalIgnoreCase))
        {
            return module is null
                ? "Access violation. This usually indicates invalid memory access, such as a null pointer, use-after-free, or buffer overrun."
                : $"Access violation in or near {module}. Check pointer lifetime, null checks, and buffer boundaries around the faulting stack.";
        }

        return description is null
            ? "Debugger analysis completed. Review the faulting stack and raw debugger output."
            : $"{description}. Review the faulting stack and raw debugger output.";
    }

    private static string? DescribeException(string? code) =>
        code?.ToUpperInvariant() switch
        {
            "0XC0000005" => "Access violation",
            "0XE0434352" => ".NET CLR exception",
            "0X80000003" => "Breakpoint",
            "0XC0000409" => "Stack buffer overrun",
            "0XC00000FD" => "Stack overflow",
            _ => code is null ? null : "Windows exception"
        };

    private static string? CleanModuleName(string? module)
    {
        if (string.IsNullOrWhiteSpace(module))
        {
            return null;
        }

        var parts = module.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[^1] : module.Trim();
    }

    private static string? NormalizeExceptionCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();
        return trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToUpperInvariant()
            : $"0x{trimmed}".ToUpperInvariant();
    }

    private static string? FirstMatch(string text, Regex regex)
    {
        var match = regex.Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"EXCEPTION_CODE:\s+\((?:NTSTATUS\)?\s*)?(0x[0-9a-fA-F]+|[0-9a-fA-F]{8})", RegexOptions.IgnoreCase)]
    private static partial Regex ExceptionCodeRegex();

    [GeneratedRegex(@"EXCEPTION_CODE_STR:\s+(0x[0-9a-fA-F]+|[0-9a-fA-F]{8})", RegexOptions.IgnoreCase)]
    private static partial Regex ExceptionCodeStrRegex();

    [GeneratedRegex(@"FAULTING_MODULE:\s+([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FaultingModuleRegex();

    [GeneratedRegex(@"IMAGE_NAME:\s+([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ImageNameRegex();

    [GeneratedRegex(@"MODULE_NAME:\s+([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ModuleNameRegex();

    [GeneratedRegex(@"Probably caused by\s+:\s+([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ProbablyCausedByRegex();

    [GeneratedRegex(@"FAULTING_THREAD:\s+([0-9a-fA-F]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FaultingThreadRegex();

    [GeneratedRegex(@"^\s*[0-9a-fA-F`]{8,}\s+[0-9a-fA-F`]{8,}\s+(?<symbol>[A-Za-z0-9_.$<>?@~\-]+![^\s+]+|[A-Za-z0-9_.$<>?@~\-]+)(?:\+0x?[0-9a-fA-F]+)?", RegexOptions.Multiline)]
    private static partial Regex StackFrameRegex();
}
