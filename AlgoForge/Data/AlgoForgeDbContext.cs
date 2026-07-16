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
        public DbSet<FaceDetection> FaceDetections => Set<FaceDetection>();
        public DbSet<FaceCluster> FaceClusters => Set<FaceCluster>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

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

            builder.Entity<Photo>()
                .HasOne(p => p.UploadedByUser)
                .WithMany()
                .HasForeignKey(p => p.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Organisation>()
                .HasOne(o => o.AdminUser)
                .WithMany()
                .HasForeignKey(o => o.AdminUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<FaceDetection>()
                .HasOne(d => d.FaceCluster)
                .WithMany(c => c.Detections)
                .HasForeignKey(d => d.FaceClusterId)
                .OnDelete(DeleteBehavior.Restrict);

            // FaceCluster.LinkedAttendee and FaceCluster.IdentifiedByUser point at different
            // tables, but both need Restrict to keep deletes explicit rather than cascading
            // through an identification decision.
            builder.Entity<FaceCluster>()
                .HasOne(c => c.LinkedAttendee)
                .WithMany()
                .HasForeignKey(c => c.LinkedAttendeeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<FaceCluster>()
                .HasOne(c => c.IdentifiedByUser)
                .WithMany()
                .HasForeignKey(c => c.IdentifiedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
