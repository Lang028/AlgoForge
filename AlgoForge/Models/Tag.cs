namespace AlgoForge.Models
{
    // D6: one consent decision. Tags hang off PersonDetection, never off Photo -- two
    // people in the same photo make independent consent decisions. Tags reference the
    // Attendee record (D3), not User directly, so an unclaimed attendee's tags stay
    // Suggested forever.
    //
    // Invariant: a Tag may only ever reference a detection with IsTaggable = true
    // (PERSON_MATCHING_PLAN.md 6). Someone who never clearly faced the camera cannot be
    // named, so the blurry figure in the background is never anyone's consent decision to
    // make. Enforced in PersonPipelineService.CreateSuggestedTagsAsync.
    public class Tag
    {
        public Guid Id { get; set; }

        public Guid PersonDetectionId { get; set; }
        public PersonDetection? PersonDetection { get; set; }

        public Guid TaggedAttendeeId { get; set; }
        public Attendee? TaggedAttendee { get; set; }

        public TagStatus Status { get; set; } = TagStatus.Suggested;
        public TagOrigin Origin { get; set; } = TagOrigin.ClusterMatch;

        public Guid CreatedByUserId { get; set; }
        public ApplicationUser? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
    }
}
