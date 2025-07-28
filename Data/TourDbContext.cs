using Microsoft.EntityFrameworkCore;
using TouristApp.Models;

namespace TouristApp.Data
{
    public class TourDbContext : DbContext
    {
        public TourDbContext(DbContextOptions<TourDbContext> options)
            : base(options)
        {
        }

        public DbSet<Tour> Tours { get; set; }
        public DbSet<Schedule> Schedules { get; set; }
    }
}
