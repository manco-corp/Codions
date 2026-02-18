namespace Codions.Contracts.Enums;

public enum JobStatus
{
    Created,
    HydratingContext,
    Queued,
    Running,
    LocalGatesRunning,
    CreatingPR,
    CompletedSuccess,
    CompletedFailed
}
