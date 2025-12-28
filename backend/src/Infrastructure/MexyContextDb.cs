
using Microsoft.EntityFrameworkCore;
using MexyApp.Models;

namespace MexyApp.Api.Domain
{
    public sealed class MexyContext : DbContext
    {
        public MexyContext(DbContextOptions<MexyContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema("public"); // explícito para Supabase

            modelBuilder.Entity<User>(b =>
            {
                b.ToTable("Users");
                b.HasKey(u => u.Id);

                b.Property(u => u.Username).HasMaxLength(100).IsRequired();
                b.Property(u => u.Email).HasMaxLength(256).IsRequired();
                b.HasIndex(u => u.Email).IsUnique();

                b.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();

                b.Property(u => u.Status)
                 .HasConversion<string>()
                 .HasMaxLength(20)
                 .IsRequired();

                // Relación 1..N usando backing field "_userRoles" (no hay propiedad de navegación pública)
                b.HasMany<UserRole>("_userRoles")
                 .WithOne(ur => ur.User)
                 .HasForeignKey(ur => ur.UserId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.Navigation("_userRoles")
                 .UsePropertyAccessMode(PropertyAccessMode.Field);
            });

            modelBuilder.Entity<UserRole>(b =>
            {
                b.ToTable("UserRoles");

                // Unicidad por (UserId, Role)
                b.HasKey(ur => new { ur.UserId, ur.Role });

                b.Property(ur => ur.Role)
                 .HasConversion<string>()
                 .HasMaxLength(50)
                 .IsRequired();
            });
        }
    }
}
