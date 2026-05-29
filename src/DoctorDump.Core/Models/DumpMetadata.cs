namespace DoctorDump.Core.Models;

public sealed record DumpMetadata
{
    public required Guid DumpId { get; init; }
    public required string ProcessName { get; init; }
    public required int Pid { get; init; }
    public string? ProcessPath { get; init; }
    public required string Architecture { get; init; }
    public required DateTimeOffset CapturedAtUtc { get; init; }
    public required string CaptureReason { get; init; }
    public string? ExceptionCode { get; init; }
    public required string DumpType { get; init; }
    public required string DumpFilePath { get; init; }
    public required string MachineName { get; init; }
    public required string OsVersion { get; init; }
    public required string DoctorDumpVersion { get; init; }
}

