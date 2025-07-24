namespace TouristApp.Models
{
    public class PystravelTourModel
    {
        public string TourName { get; set; }
        public string Price { get; set; }
        public string ImageUrl { get; set; }
        public string TourDetailUrl { get; set; }
        public string DepartureDate { get; set; } // Nếu lấy được
        public string StartingPoint { get; set; }
        public string Duration { get; set; }

        public List<ScheduleDay> Schedule { get; set; } = new();  // Lịch trình từng ngày
        public Dictionary<string, string> ImportantNotes { get; set; } = new(); // Điều khoản & ghi chú
    }

    public class ScheduleDay
    {
        public string DayTitle { get; set; }
        public string DayContent { get; set; }
    }
}
