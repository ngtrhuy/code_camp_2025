// TourImportService.cs
using Microsoft.EntityFrameworkCore;
using TouristApp.Data;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class TourImportService
    {
        private readonly TourDbContext _context;

        public TourImportService(TourDbContext context)
        {
            _context = context;
        }

        public async Task ImportTours(List<StandardTourModel> tours)
        {
            foreach (var tourModel in tours)
            {
                // Kiểm tra xem tour đã tồn tại chưa
                var existingTour = await _context.Tours
                    .FirstOrDefaultAsync(t => t.TourDetailUrl == tourModel.TourDetailUrl);

                if (existingTour == null)
                {
                    // Tạo tour mới
                    var tour = new Tour
                    {
                        TourName = tourModel.TourName,
                        TourCode = tourModel.TourCode,
                        Price = tourModel.Price,
                        ImageUrl = tourModel.ImageUrl,
                        DepartureLocation = tourModel.DepartureLocation,
                        Duration = tourModel.Duration,
                        TourDetailUrl = tourModel.TourDetailUrl,
                        DepartureDates = string.Join(",", tourModel.DepartureDates),
                        ImportantNotes = System.Text.Json.JsonSerializer.Serialize(tourModel.ImportantNotes),
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.Tours.Add(tour);
                    await _context.SaveChangesAsync();

                    // Thêm lịch trình
                    foreach (var scheduleItem in tourModel.Schedule)
                    {
                        var schedule = new Schedule
                        {
                            TourId = tour.Id,
                            DayTitle = scheduleItem.DayTitle,
                            DayContent = scheduleItem.DayContent
                        };
                        _context.Schedules.Add(schedule);
                    }

                    await _context.SaveChangesAsync();
                }
            }
        }
    }
}