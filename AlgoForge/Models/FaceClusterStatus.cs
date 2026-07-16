namespace AlgoForge.Models
{
    // D-FaceCluster: a cluster starts Unidentified (just "same face, unknown who") and becomes
    // Identified once a coordinator/photographer links it to a real Attendee.
    public enum FaceClusterStatus
    {
        Unidentified,
        Identified
    }
}
