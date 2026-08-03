namespace AlgoForge.Models
{
    // OPEN-5 (v1 simplification): synchronous processing means failure is just "try again on
    // next upload/refresh" rather than a queue retry/dead-letter policy.
    public enum PhotoFaceProcessingStatus
    {
        Pending,
        Processed,
        Failed,

        // Uploaded to a link-shared gallery, where detection never runs. A distinct value
        // rather than reusing Processed, which would claim a pass happened, or leaving it
        // Pending, which the worker's startup sweep would pick up and run -- quietly doing
        // the one thing that mode exists to not do.
        NotApplicable
    }
}
