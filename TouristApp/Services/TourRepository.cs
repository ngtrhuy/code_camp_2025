using MySql.Data.MySqlClient;
using System.Text.Encodings.Web;
using System.Text.Json;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class TourRepository
    {
        private readonly string _connectionString;

        public TourRepository(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        public int SaveTours(List<StandardTourModel> tours)
        {
            int savedCount = 0;

            using var conn = new MySqlConnection(_connectionString);
            conn.Open();

            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            foreach (var tour in tours)
            {
                try
                {
                    var tourCode = string.IsNullOrWhiteSpace(tour.TourCode)
                        ? "TOUR" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()
                        : tour.TourCode.Trim();

                    var sourceSite = (tour.SourceSite ?? "").Trim();
                    var duration = (tour.Duration ?? "");
                    if (duration.Length > 255) duration = duration[..255];

                    var upsertSql = @"
INSERT INTO tours (
  tour_name, tour_code, price, image_url,
  departure_location, duration, tour_detail_url,
  departure_dates, important_notes, source_site
) VALUES (
  @tour_name, @tour_code, @price, @image_url,
  @departure_location, @duration, @tour_detail_url,
  @departure_dates, @important_notes, @source_site
)
ON DUPLICATE KEY UPDATE
  tour_name = VALUES(tour_name),
  price = VALUES(price),
  image_url = VALUES(image_url),
  departure_location = VALUES(departure_location),
  duration = VALUES(duration),
  tour_detail_url = VALUES(tour_detail_url),
  departure_dates = VALUES(departure_dates),
  important_notes = VALUES(important_notes);";

                    using (var cmd = new MySqlCommand(upsertSql, conn))
                    {
                        cmd.Parameters.AddWithValue("@tour_name", tour.TourName ?? "");
                        cmd.Parameters.AddWithValue("@tour_code", tourCode);
                        cmd.Parameters.AddWithValue("@price", tour.Price ?? "");
                        cmd.Parameters.AddWithValue("@image_url", tour.ImageUrl ?? "");
                        cmd.Parameters.AddWithValue("@departure_location", tour.DepartureLocation ?? "");
                        cmd.Parameters.AddWithValue("@duration", duration);
                        cmd.Parameters.AddWithValue("@tour_detail_url", tour.TourDetailUrl ?? "");
                        cmd.Parameters.AddWithValue("@departure_dates",
                            JsonSerializer.Serialize(tour.DepartureDates ?? new List<string>(), jsonOptions));
                        cmd.Parameters.AddWithValue("@important_notes",
                            JsonSerializer.Serialize(tour.ImportantNotes ?? new Dictionary<string, string>(), jsonOptions));
                        cmd.Parameters.AddWithValue("@source_site", sourceSite);

                        cmd.ExecuteNonQuery();
                    }

                    // Lấy id theo (site, code)
                    int tourId;
                    using (var find = new MySqlCommand(
                        "SELECT id FROM tours WHERE source_site=@site AND tour_code=@code LIMIT 1", conn))
                    {
                        find.Parameters.AddWithValue("@site", sourceSite);
                        find.Parameters.AddWithValue("@code", tourCode);
                        tourId = Convert.ToInt32(find.ExecuteScalar());
                    }

                    // Làm mới schedules
                    using (var del = new MySqlCommand("DELETE FROM schedules WHERE tour_id=@id", conn))
                    {
                        del.Parameters.AddWithValue("@id", tourId);
                        del.ExecuteNonQuery();
                    }

                    foreach (var schedule in tour.Schedule ?? new List<TourScheduleItem>())
                    {
                        using var scheduleCmd = new MySqlCommand(@"
                            INSERT INTO schedules (tour_id, day_title, day_content)
                            VALUES (@tour_id, @day_title, @day_content);", conn);

                        scheduleCmd.Parameters.AddWithValue("@tour_id", tourId);
                        scheduleCmd.Parameters.AddWithValue("@day_title", schedule.DayTitle ?? "");
                        scheduleCmd.Parameters.AddWithValue("@day_content", schedule.DayContent ?? "");
                        scheduleCmd.ExecuteNonQuery();
                    }

                    savedCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Lỗi khi lưu tour: {tour.TourName} - {ex.Message}");
                }
            }

            return savedCount;
        }
    }
}
