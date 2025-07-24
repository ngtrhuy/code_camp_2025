namespace TouristApp.Models
{
    public class TourModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Url { get; set; }
        public string ImageUrl { get; set; }
        public string Route { get; set; }
        public List<string>? DepartureDates { get; set; }

        public string Duration { get; set; }
        public string PriceOld { get; set; }
        public string PriceNew { get; set; }
        public string ReviewScore { get; set; }
        public string ReviewText { get; set; }
        public string ReviewCount { get; set; }
        public string Promotion { get; set; }
        public string Gift { get; set; }
        public List<TourItineraryItem> Itinerary { get; set; }
        public Dictionary<string, List<string>> ServicePolicies { get; set; }
        public class TourItineraryItem
        {
            public string DayTitle { get; set; }
            public string Description { get; set; }
        }



    }

}
