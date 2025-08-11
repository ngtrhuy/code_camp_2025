namespace TouristApp.Models
{
    public class PageConfigModel
    {
        public int Id { get; set; }
        public string BaseDomain { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;

        public string TourName { get; set; } = string.Empty;
        public string TourCode { get; set; } = string.Empty;
        public string TourPrice { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string DepartureLocation { get; set; } = string.Empty;
        public string DepartureDate { get; set; } = string.Empty;
        public string TourDuration { get; set; } = string.Empty;
        public string PagingType { get; set; } = "none";
        public string TourDetailUrl { get; set; } = string.Empty;
        public string TourDetailDayTitle { get; set; } = string.Empty;
        public string TourDetailDayContent { get; set; } = string.Empty;
        public string TourDetailNote { get; set; } = string.Empty;
        public string CrawlType { get; set; } = string.Empty;
        public string TourListSelector { get; set; } = string.Empty;
        public string ImageAttr { get; set; } = "src";
        public string TourDetailAttr { get; set; } = "href";
        public DateTime CreatedAt { get; set; }
        public string LoadMoreButtonSelector { get; set; } = string.Empty;
        public string LoadMoreType { get; set; } = "class";
        // Removed duplicate PagingType property definition
    }
}
    