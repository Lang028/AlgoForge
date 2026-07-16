namespace AlgoForge.Models
{
    // OPEN-5 (v1 simplification): synchronous processing means failure is just "try again on
    // next upload/refresh" rather than a queue retry/dead-letter policy.
    public enum PhotoFaceProcessingStatus
    {
        Pending,
        Processed,
        Failed
    }
}
