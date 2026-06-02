using System.Diagnostics;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var analyzerProject = Path.Combine(repoRoot, "src", "DoctorDump.Analyzer", "DoctorDump.Analyzer.csproj");

if (!File.Exists(analyzerProject))
{
    Console.Error.WriteLine($"Analyzer project was not found: {analyzerProject}");
    return 2;
}

var startInfo = new ProcessStartInfo("dotnet")
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    WorkingDirectory = repoRoot
};
startInfo.ArgumentList.Add("run");
startInfo.ArgumentList.Add("--project");
startInfo.ArgumentList.Add(analyzerProject);
startInfo.ArgumentList.Add("--");
startInfo.ArgumentList.Add("--self-test");

using var analyzer = Process.Start(startInfo);
if (analyzer is null)
{
    Console.Error.WriteLine("Could not start analyzer self-test.");
    return 2;
}

var stdout = await analyzer.StandardOutput.ReadToEndAsync();
var stderr = await analyzer.StandardError.ReadToEndAsync();
await analyzer.WaitForExitAsync();

Console.Write(stdout);
if (!string.IsNullOrWhiteSpace(stderr))
{
    Console.Error.Write(stderr);
}

return analyzer.ExitCode;

static string FindRepoRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "DoctorDump.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate DoctorDump.slnx from test output directory.");
}
