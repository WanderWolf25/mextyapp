
using Microsoft.EntityFrameworkCore;
using MexyApp.Models; // Aquí está tu clase User

namespace MexyApp.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
    }
}
