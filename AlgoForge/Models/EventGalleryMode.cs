namespace AlgoForge.Models
{
    // How an event's photographs reach the people in them. Chosen once, when the event is
    // created, and never changed afterwards -- see Event.GalleryMode for why.
    public enum EventGalleryMode
    {
        // The full product: attendees are invited, faces are detected and clustered, and a
        // name only ever appears on a photograph once the person it belongs to confirms it.
        // Default, and the value existing events already hold.
        Consent = 0,

        // A plain gallery behind a link. Anyone holding the link can look and download;
        // there are no attendees, no tags and no connection requests.
        //
        // Face detection does not run on these events at all. That is not a simplification
        // -- it is the only defensible setting. Detection stores a face embedding for every
        // person in every photograph, and here there is nobody to ask and no mechanism to
        // ask them with, so there would be no basis for holding it. An event that never
        // offers consent must never collect what consent exists to authorise.
        LinkShared = 1
    }
}
