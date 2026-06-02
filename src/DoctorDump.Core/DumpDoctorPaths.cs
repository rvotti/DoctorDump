namespace DoctorDump.Core;

public static class DumpDoctorPaths
{
    public static string AppRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpDoctor");

    public static string DefaultDumpRoot => Path.Combine(AppRoot, "dumps");

    public static string ConfigRoot => Path.Combine(AppRoot, "config");

    public static string LogRoot => Path.Combine(AppRoot, "logs");

    public static string GetDatedDumpFolder(DateTimeOffset capturedAt) =>
        Path.Combine(DefaultDumpRoot, capturedAt.ToLocalTime().ToString("yyyy-MM-dd"));

    public static string GetDatedDumpFolder(string dumpRoot, DateTimeOffset capturedAt) =>
        Path.Combine(dumpRoot, capturedAt.ToLocalTime().ToString("yyyy-MM-dd"));
}
