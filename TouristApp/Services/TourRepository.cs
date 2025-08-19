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

            // ✅ Cấu hình JSON để giữ nguyên ký tự UTF-8 (tiếng Việt)
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            foreach (var tour in tours)
            {
                try
                {
                    var insertCmd = new MySqlCommand(@"
                        INSERT INTO tours (
                            tour_name, tour_code, price, image_url,
                            departure_location, duration, tour_detail_url,
                            departure_dates, important_notes
                        )
                        VALUES (
                            @tour_name, @tour_code, @price, @image_url,
                            @departure_location, @duration, @tour_detail_url,
                            @departure_dates, @important_notes
                        );
                        SELECT LAST_INSERT_ID();", conn);

                    insertCmd.Parameters.AddWithValue("@tour_name", tour.TourName ?? "");
                    insertCmd.Parameters.AddWithValue("@tour_code", tour.TourCode ?? "");
                    insertCmd.Parameters.AddWithValue("@price", tour.Price ?? "");
                    insertCmd.Parameters.AddWithValue("@image_url", tour.ImageUrl ?? "");
                    insertCmd.Parameters.AddWithValue("@departure_location", tour.DepartureLocation ?? "");
                    insertCmd.Parameters.AddWithValue("@duration", tour.Duration ?? "");
                    insertCmd.Parameters.AddWithValue("@tour_detail_url", tour.TourDetailUrl ?? "");

                    // ✅ Serialize với UTF-8
                    insertCmd.Parameters.AddWithValue("@departure_dates", JsonSerializer.Serialize(tour.DepartureDates, jsonOptions));
                    insertCmd.Parameters.AddWithValue("@important_notes", JsonSerializer.Serialize(tour.ImportantNotes, jsonOptions));

                    int tourId = Convert.ToInt32(insertCmd.ExecuteScalar());
                    savedCount++;

                    // ✅ Insert lịch trình
                    foreach (var schedule in tour.Schedule)
                    {
                        var scheduleCmd = new MySqlCommand(@"
                            INSERT INTO schedules (tour_id, day_title, day_content)
                            VALUES (@tour_id, @day_title, @day_content);", conn);

                        scheduleCmd.Parameters.AddWithValue("@tour_id", tourId);
                        scheduleCmd.Parameters.AddWithValue("@day_title", schedule.DayTitle ?? "");
                        scheduleCmd.Parameters.AddWithValue("@day_content", schedule.DayContent ?? "");

                        scheduleCmd.ExecuteNonQuery();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Lỗi khi lưu tour: {tour.TourName} - {ex.Message}");
                }
            }

            return savedCount;
        }


        public bool IsTourExists(string? code, string? url)
        {
            using var conn = new MySqlConnection(_connectionString);
            conn.Open();
            var cmd = new MySqlCommand(
                @"SELECT COUNT(*) FROM tours WHERE tour_detail_url = @tourDetailUrl", conn);
            cmd.Parameters.AddWithValue("@tourDetailUrl", url ?? "");
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }



    }
}
