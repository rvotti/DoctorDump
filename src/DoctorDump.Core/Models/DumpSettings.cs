namespace DoctorDump.Core.Models;

public sealed record DumpSettings
{
    public string DumpType { get; init; } = "mini";
    public string SymbolPath { get; init; } = @"srv*C:\Symbols*https://msdl.microsoft.com/download/symbols";
    public string OutputDirectory { get; init; } = DoctorDump.Core.DumpDoctorPaths.DefaultDumpRoot;
    public bool AutoAnalyze { get; init; } = true;
    public int KeepDumpsForDays { get; init; } = 30;
}
