namespace AlgoForge.Models
{
    // D13: Draft -> Live -> PostEvent -> Archived. Uploads only in Live;
    // tag review/connections open in Live and PostEvent; Archived is read-only.
    public enum EventStatus
    {
        Draft,
        Live,
        PostEvent,
        Archived
    }
}
