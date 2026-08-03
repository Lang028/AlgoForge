using AlgoForge.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Data
{
    public class AlgoForgeDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
    {
        public AlgoForgeDbContext(DbContextOptions<AlgoForgeDbContext> options)
            : base(options)
        {
        }

        public DbSet<Organisation> Organisations => Set<Organisation>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<EventMembership> EventMemberships => Set<EventMembership>();
        public DbSet<Attendee> Attendees => Set<Attendee>();
        public DbSet<Photo> Photos => Set<Photo>();
        public DbSet<PersonCluster> PersonClusters => Set<PersonCluster>();
        public DbSet<PersonDetection> PersonDetections => Set<PersonDetection>();
        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<Connection> Connections => Set<Connection>();
        public DbSet<DelegateInvite> DelegateInvites => Set<DelegateInvite>();
        public DbSet<OrganisationHandover> OrganisationHandovers => Set<OrganisationHandover>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Same reason as EventMembership below: several paths reach ApplicationUser
            // and Event, so deletes are restricted rather than cascading.
            builder.Entity<DelegateInvite>()
                .HasOne(d => d.ClaimedByUser)
                .WithMany()
                .HasForeignKey(d => d.ClaimedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<DelegateInvite>()
                .HasOne(d => d.Event)
                .WithMany()
                .HasForeignKey(d => d.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<DelegateInvite>()
                .HasOne(d => d.Attendee)
                .WithMany()
                .HasForeignKey(d => d.AttendeeId)
                .OnDelete(DeleteBehavior.Cascade);

            // EventMembership.User and EventMembership.GrantedByUser both point at
            // ApplicationUser -- restrict both to avoid multiple SQL Server cascade paths.
            builder.Entity<EventMembership>()
                .HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<EventMembership>()
                .HasOne(m => m.GrantedByUser)
                .WithMany()
                .HasForeignKey(m => m.GrantedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Attendee>()
                .HasOne(a => a.ClaimedByUser)
                .WithMany()
                .HasForeignKey(a => a.ClaimedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // One row per person per event, enforced by the database rather than only by
            // the import paths. Two records for one attendee splits their identity in half:
            // detections cluster onto one, the tags they confirm attach to the other.
            builder.Entity<Attendee>()
                .HasIndex(a => new { a.EventId, a.Email })
                .IsUnique();

            builder.Entity<Photo>()
                .HasOne(p => p.UploadedByUser)
                .WithMany()
                .HasForeignKey(p => p.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<OrganisationHandover>()
                .HasOne(h => h.Event)
                .WithMany()
                .HasForeignKey(h => h.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            // Event.OrganisationId is a required FK with no explicit behavior configured --
            // every other relationship in this file is deliberately Restrict, so an
            // Organisation delete doesn't silently cascade through Events -> Photos (which
            // is itself Restrict) and blow up at the DB level instead of a friendly message.
            builder.Entity<Event>()
                .HasOne(e => e.Organisation)
                .WithMany(o => o.Events)
                .HasForeignKey(e => e.OrganisationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Organisation>()
                .HasOne(o => o.AdminUser)
                .WithMany()
                .HasForeignKey(o => o.AdminUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Tag.CreatedByUser and Tag.TaggedAttendee both sit off ApplicationUser's FK
            // graph indirectly -- restrict both so SQL Server doesn't reject multiple
            // cascade paths, same pattern as EventMembership above.
            builder.Entity<Tag>()
                .HasOne(t => t.CreatedByUser)
                .WithMany()
                .HasForeignKey(t => t.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Tag>()
                .HasOne(t => t.TaggedAttendee)
                .WithMany()
                .HasForeignKey(t => t.TaggedAttendeeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Tag>()
                .HasOne(t => t.PersonDetection)
                .WithMany()
                .HasForeignKey(t => t.PersonDetectionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Event -> Photo cascades by convention (Photo.EventId is required and was
            // never configured otherwise). Left alone, that collides with PersonCluster's
            // SetNull below: SQL Server refuses a table reachable from Event by two
            // different delete actions (Photo's Cascade and PersonCluster's SetNull, both
            // arriving at PersonDetections). Restrict, same pattern as every other
            // multi-path relationship in this file -- there is no Delete Event action in
            // the app today, so this costs nothing.
            builder.Entity<Photo>()
                .HasOne(p => p.Event)
                .WithMany(e => e.Photos)
                .HasForeignKey(p => p.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            // Reclustering deletes and rebuilds unidentified clusters on every run, so
            // detections must survive their cluster being dropped -- SetNull, never
            // Cascade. Cascade here would delete the detections and, through the Tag
            // cascade above, silently destroy consent records on every recluster.
            builder.Entity<PersonDetection>()
                .HasOne(d => d.PersonCluster)
                .WithMany(c => c.Detections)
                .HasForeignKey(d => d.PersonClusterId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<PersonDetection>()
                .HasOne(d => d.Photo)
                .WithMany(p => p.PersonDetections)
                .HasForeignKey(d => d.PhotoId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PersonCluster>()
                .HasOne(c => c.LinkedAttendee)
                .WithMany()
                .HasForeignKey(c => c.LinkedAttendeeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<PersonCluster>()
                .HasOne(c => c.IdentifiedByUser)
                .WithMany()
                .HasForeignKey(c => c.IdentifiedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Every attendee-facing query filters on IsTaggable, and the review grid
            // pages by cluster -- both are hot paths once an event has a few thousand
            // detections.
            builder.Entity<PersonDetection>()
                .HasIndex(d => new { d.PhotoId, d.IsTaggable });

            builder.Entity<PersonCluster>()
                .HasIndex(c => new { c.EventId, c.Status });

            // Both ends point at ApplicationUser -- restrict, same cascade-path reason as above.
            builder.Entity<Connection>()
                .HasOne(c => c.Requester)
                .WithMany()
                .HasForeignKey(c => c.RequesterId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Connection>()
                .HasOne(c => c.Receiver)
                .WithMany()
                .HasForeignKey(c => c.ReceiverId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Connection>()
                .HasOne(c => c.MetAtEvent)
                .WithMany()
                .HasForeignKey(c => c.MetAtEventId)
                .OnDelete(DeleteBehavior.Restrict);

            // D10: one connection per pair of people, whichever direction it was asked in.
            builder.Entity<Connection>()
                .HasIndex(c => new { c.PairLowId, c.PairHighId })
                .IsUnique();

            builder.Entity<Connection>()
                .HasIndex(c => c.ResponseToken);
        }
    }
}
