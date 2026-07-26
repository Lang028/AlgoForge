namespace AlgoForge.Models
{
    // One person recurring across an event's photos. Replaces FaceCluster.
    //
    // Clustering is no longer incremental. The Python pipeline reclusters the whole event
    // from scratch on every run (PERSON_MATCHING_PLAN.md 5: at one-day-event scale this is
    // both cheaper and far simpler than incremental assignment, and it lets a late photo
    // fix an earlier mistake instead of inheriting it).
    //
    // That reopens OPEN-4 -- re-clustering must not change the Id of a cluster someone has
    // already identified, or the Tags hanging off it are orphaned and consent data is
    // destroyed. The answer lives in PersonPipelineService.ReclusterEventAsync: identified
    // clusters are matched onto the new grouping by membership overlap and keep their Id.
    // Stability is therefore a property of reconciliation, not of the clustering algorithm.
    public class PersonCluster
    {
        public Guid Id { get; set; }

        public Guid EventId { get; set; }
        public Event? Event { get; set; }

        public PersonClusterStatus Status { get; set; } = PersonClusterStatus.Unidentified;

        // Highest-quality Tier A face in the cluster -- the review grid's cover crop.
        public Guid? AnchorDetectionId { get; set; }

        // False for clusters built purely from Tier B detections (someone who never faced
        // the camera, staff shot from behind). These are never surfaced to anyone,
        // including the photographer's identify flow.
        public bool HasTaggableDetection { get; set; }

        // Links to the Attendee record, not to a User (D3). An attendee exists on the
        // invitee list before they have an account, and identification must work then.
        public Guid? LinkedAttendeeId { get; set; }
        public Attendee? LinkedAttendee { get; set; }

        public Guid? IdentifiedByUserId { get; set; }
        public ApplicationUser? IdentifiedByUser { get; set; }

        public ICollection<PersonDetection> Detections { get; set; } = new List<PersonDetection>();
    }
}
