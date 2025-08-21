namespace TouristApp.Models
{
    public class StandardTourModel
    {
        public string TourName { get; set; } = string.Empty;
        public string TourCode { get; set; } = string.Empty;
        public string Price { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string DepartureLocation { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string TourDetailUrl { get; set; } = string.Empty;

        public List<string> DepartureDates { get; set; } = new List<string>();
        public Dictionary<string, string> ImportantNotes { get; set; } = new Dictionary<string, string>();

        public string? SourceSite { get; set; }

        public List<TourScheduleItem> Schedule { get; set; } = new List<TourScheduleItem>();
    }

    public class TourScheduleItem
    {
        public int Id { get; set; }
        public string DayTitle { get; set; } = string.Empty;
        public string DayContent { get; set; } = string.Empty;
    }
}

