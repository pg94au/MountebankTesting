namespace Tests;

public class JobStatus
{
    public string Id { get; set; }
    public uint Errors { get; set; }
    public uint Warnings { get; set; }
    public bool Deferred { get; set; }
}