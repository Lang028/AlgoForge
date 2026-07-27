using AlgoForge.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Organisation> Organisations => Set<Organisation>();
        public DbSet<Event> Events => Set<Event>();
        public DbSet<EventMembership> EventMemberships => Set<EventMembership>();
        public DbSet<Invitation> Invitations => Set<Invitation>();
        public DbSet<Album> Albums => Set<Album>();
        public DbSet<Photo> Photos => Set<Photo>();
        public DbSet<FaceDetection> FaceDetections => Set<FaceDetection>();
        public DbSet<FaceCluster> FaceClusters => Set<FaceCluster>();
        public DbSet<Tag> Tags => Set<Tag>();
        public DbSet<Connection> Connections => Set<Connection>();
        public DbSet<Comment> Comments => Set<Comment>();
        public DbSet<Attendee> Attendees => Set<Attendee>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // A user can only hold one role per event (no duplicate memberships)
            builder.Entity<EventMembership>()
                .HasIndex(m => new { m.UserId, m.EventId })
                .IsUnique();

            builder.Entity<EventMembership>()
                .HasOne(m => m.User)
                .WithMany(u => u.Memberships)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<EventMembership>()
                .HasOne(m => m.Event)
                .WithMany(e => e.Memberships)
                .HasForeignKey(m => m.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<FaceCluster>()
                .HasOne(c => c.IdentifiedEventMembership)
                .WithMany()
                .HasForeignKey(c => c.IdentifiedEventMembershipId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Photo>()
                .HasQueryFilter(p => !p.IsDeleted);

            builder.Entity<Invitation>()
                .HasIndex(i => new { i.EventId, i.Email });

            // Attendee -> Event: Restrict, to avoid a second cascade path into the
            // same table alongside FaceCluster's own Event relationship.
            builder.Entity<Attendee>()
                .HasOne(a => a.Event)
                .WithMany(e => e.Attendees)
                .HasForeignKey(a => a.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            // Attendee -> ClaimedByUser: Restrict, same reasoning as EventMembership.User.
            builder.Entity<Attendee>()
                .HasOne(a => a.ClaimedByUser)
                .WithMany()
                .HasForeignKey(a => a.ClaimedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Attendee>()
                .HasIndex(a => new { a.EventId, a.Email });
        }
    }
}
