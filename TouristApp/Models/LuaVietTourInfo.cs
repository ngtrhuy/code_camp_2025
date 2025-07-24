namespace TouristApp.Models;
public class LuaVietTourInfo
{
    public string TourName { get; set; }
    public string TourCode { get; set; }
    public string Price { get; set; }
    public string ImageUrl { get; set; }
    public List<string> DepartureDates { get; set; }
    public string DepartureLocation { get; set; }
    public string Duration { get; set; }

    public string TourDetailUrl { get; set; }
    public List<TourDaySchedule> Schedule { get; set; } = new();
    public Dictionary<string, string> ImportantNotes { get; set; } = new();

}

public class TourDaySchedule
{
    public string DayTitle { get; set; }
    public string DayContent { get; set; }
}
