namespace DoctorDump.Core.Models;

public sealed record AnalysisResult
{
    public required Guid DumpId { get; init; }
    public required string Status { get; init; }
    public string? ExceptionCode { get; init; }
    public string? ExceptionDescription { get; init; }
    public string? FaultingModule { get; init; }
    public int? FaultingThreadId { get; init; }
    public string SymbolStatus { get; init; } = "NotRun";
    public string? ProbableCause { get; init; }
    public IReadOnlyList<StackFrameInfo> CallStack { get; init; } = [];
}

public sealed record StackFrameInfo(
    int Index,
    string? Module,
    string? Function,
    string? Source,
    int? Line);

