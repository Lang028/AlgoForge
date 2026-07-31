using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Account
{
    // The attendee side of the front door.
    //
    // Attendees do not sign in where coordinators and photographers do. They arrive from a
    // link in a message, they came to look at photographs, and asking them to pick a staff
    // role on a sign-up form is the wrong question asked at the wrong moment. So the invite
    // link itself is the entry point: the album loads behind a gate, and the gate opens on
    // an email plus the link they were already given.
    //
    // The two together are the credential. The token is the unguessable half; the email is
    // what stops a forwarded link from letting a stranger claim someone else's face.
    public class AttendeeGateViewModel
    {
        [Required(ErrorMessage = "Enter the email address your invite was sent to.")]
        [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
        [Display(Name = "Your email")]
        public string Email { get; set; } = string.Empty;

        // Accepts the whole pasted URL or just the token. People paste what they were sent,
        // not the fragment a developer had in mind.
        [Required(ErrorMessage = "Paste the link you were given.")]
        [Display(Name = "Your invite link")]
        public string Link { get; set; } = string.Empty;

        /// <summary>Shown on the gate so the attendee can see they are in the right place.</summary>
        public string EventName { get; set; } = string.Empty;
    }
}
