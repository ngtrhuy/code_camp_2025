namespace TouristApp.Models
{
    public class StandardTourModel
    {
        // Thông tin cơ bản
        public string TourName { get; set; } = string.Empty;
        public string TourCode { get; set; } = string.Empty;
        public string Price { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string DepartureLocation { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string TourDetailUrl { get; set; } = string.Empty;

        // Danh sách ngày khởi hành
        public List<string> DepartureDates { get; set; } = new();

        // Lịch trình từng ngày
        public List<TourScheduleItem> Schedule { get; set; } = new();

        // Chú ý/Điều khoản: { Tiêu đề -> Nội dung }
        public Dictionary<string, string> ImportantNotes { get; set; } = new();
    }

    public class TourScheduleItem
    {
        public string DayTitle { get; set; } = string.Empty;
        public string DayContent { get; set; } = string.Empty;
    }
}
