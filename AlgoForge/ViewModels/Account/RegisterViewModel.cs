using System.ComponentModel.DataAnnotations;
using AlgoForge.Models;

namespace AlgoForge.ViewModels.Account
{
    // Registration creates an account and nothing else. It deliberately carries no event
    // and no role: those arrive only by creating an event, accepting an invitation, or
    // claiming an attendee record from an emailed token.
    //
    // The previous version let the registrant pick any event in the database and grant
    // themselves Coordinator or Photographer on it. That was written when nothing read
    // EventMembership, so it looked harmless; the moment the access rules went in it became
    // a way to hand yourself the keys to someone else's event.
    public class RegisterViewModel
    {
        // Which of the two staff roles the person is signing up as. This still grants
        // nothing: it is stored as the ActiveRole claim so the dashboard and navigation
        // open on the right thing, and access continues to come only from EventMembership.
        //
        // Only Coordinator and Photographer are offered. Attendees and delegates never
        // register their way in -- they arrive through an emailed invite link.
        [Required(ErrorMessage = "Choose whether you're signing up as an organisation or a photographer.")]
        [Display(Name = "I'm signing up as")]
        public EventRole? SignUpAs { get; set; }

        // For a photographer this is their own name. For an organisation it is the
        // organisation's name -- that is what appears on their events, so asking for a
        // personal name and then never using it would be pointless. Validated in the
        // controller, which knows which of the two was chosen.
        //
        // Deliberately nullable. The project builds with <Nullable>enable</Nullable>, and
        // ASP.NET Core treats a non-nullable reference type as implicitly [Required] --
        // no attribute needed. That made an organisation sign-up impossible: the full-name
        // input is hidden for that path but still posts an empty string, which failed the
        // implicit rule with "The Full name field is required" before the conditional
        // check below in the controller ever got a say. Same reasoning for
        // OrganisationName, which is nullable for the mirror-image reason.
        [Display(Name = "Full name")]
        public string? DisplayName { get; set; }

        /// <summary>Only used when signing up as an organisation.</summary>
        [Display(Name = "Organisation name")]
        public string? OrganisationName { get; set; }

        /// <summary>Where people should reach the organisation; defaults to the sign-up email.</summary>
        [Display(Name = "Organisation contact email")]
        public string? OrganisationContactEmail { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
