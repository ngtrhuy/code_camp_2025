using System.Collections.Generic;

namespace TouristApp.Models
{
    public class DeVietTourInfo
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Price { get; set; }
        public string Duration { get; set; }
        public string ImageUrl { get; set; }

        public string Airline { get; set; }
        public string DepartureDate { get; set; }

        public List<TourDay> Itinerary { get; set; } = new List<TourDay>();

        public string TourPriceDetail { get; set; }
        public string Included { get; set; }
        public string NotIncluded { get; set; }
        public string ChildrenPolicy { get; set; }
        public string ContractPolicy { get; set; }

        public string TourDetailUrl { get; set; } // Dự phòng cho nhu cầu mở rộng
    }

    public class TourDay
    {
        public string DayTitle { get; set; }
        public string DayContent { get; set; }
    }
}
