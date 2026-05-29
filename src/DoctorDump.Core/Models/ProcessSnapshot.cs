namespace DoctorDump.Core.Models;

public sealed record ProcessSnapshot(
    int Pid,
    string Name,
    string? Path,
    string Architecture);

