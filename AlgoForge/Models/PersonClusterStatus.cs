namespace AlgoForge.Models
{
    // A cluster starts Unidentified (just "same person, unknown who") and becomes
    // Identified once a coordinator or photographer links it to a real Attendee (D4).
    //
    // Ignored is for clusters that are real people but not event attendees -- venue
    // staff, passers-by, the barista. Marking one Ignored hides it from every flow and
    // marks its embeddings for the purge schedule, without pretending the detections
    // were wrong.
    public enum PersonClusterStatus
    {
        Unidentified,
        Identified,
        Ignored
    }
}
