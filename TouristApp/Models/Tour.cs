using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TouristApp.Models
{
    [Table("tours")]
    public class Tour
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("tour_name")]
        public string TourName { get; set; } = string.Empty;

        [Column("tour_code")]
        public string TourCode { get; set; } = string.Empty;

        [Column("price")]
        public string Price { get; set; } = string.Empty;

        [Column("image_url")]
        public string ImageUrl { get; set; } = string.Empty;

        [Column("departure_location")]
        public string DepartureLocation { get; set; } = string.Empty;

        [Column("duration")]
        public string Duration { get; set; } = string.Empty;

        [Column("tour_detail_url")]
        public string TourDetailUrl { get; set; } = string.Empty;

        [Column("departure_dates")]
        public string? DepartureDates { get; set; }

        [Column("important_notes")]
        public string? ImportantNotes { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    [Table("schedules")]
    public class Schedule
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("tour_id")]
        public int TourId { get; set; }

        [Column("day_title")]
        public string DayTitle { get; set; } = string.Empty;

        [Column("day_content")]
        public string DayContent { get; set; } = string.Empty;
    }
}
