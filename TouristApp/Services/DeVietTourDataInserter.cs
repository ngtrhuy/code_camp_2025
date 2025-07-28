using MySql.Data.MySqlClient;
using TouristApp.Models;
using System.Text;

namespace TouristApp.Services
{
    public class DeVietTourDataInserter
    {
        private readonly string _connectionString;

        public DeVietTourDataInserter(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task InsertToursAsync(List<StandardTourModel> tours)
        {
            using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            foreach (var tour in tours)
            {
                if (string.IsNullOrWhiteSpace(tour.TourName) || string.IsNullOrWhiteSpace(tour.Price))
                    continue;

                // Kiểm tra tour đã tồn tại
                using (var checkCmd = new MySqlCommand("SELECT EXISTS(SELECT 1 FROM tours WHERE tour_name = @name)", connection))
                {
                    checkCmd.Parameters.AddWithValue("@name", tour.TourName);
                    var exists = Convert.ToBoolean(await checkCmd.ExecuteScalarAsync());

                    if (exists)
                    {
                        Console.WriteLine($"⚠️ Tour '{tour.TourName}' đã tồn tại. Bỏ qua insert.");
                        continue;
                    }
                }

                // 🔧 Format departure_dates: bỏ dấu "-" và chuẩn hóa cách nhau bằng dấu phẩy
                string departureDatesText = "";
                if (tour.DepartureDates != null && tour.DepartureDates.Any())
                {
                    var cleanedDates = tour.DepartureDates
                        .Select(d => d.Replace("-", "").Trim());
                    departureDatesText = string.Join(", ", cleanedDates);
                }

                // 🔧 Format important_notes thành string text
                var notesBuilder = new StringBuilder();
                if (tour.ImportantNotes != null)
                {
                    foreach (var note in tour.ImportantNotes)
                    {
                        notesBuilder.AppendLine($"{note.Key}: {note.Value}");
                    }
                }
                string importantNotesText = notesBuilder.ToString().Trim();

                int insertedTourId = 0;

                try
                {
                    using var cmd = new MySqlCommand(@"
                        INSERT INTO tours (
                            tour_name, tour_code, price, image_url,
                            departure_location, duration, tour_detail_url,
                            departure_dates, important_notes, created_at
                        ) VALUES (
                            @name, @code, @price, @img,
                            @location, @duration, @url,
                            @departureDates, @importantNotes, NOW()
                        );
                        SELECT LAST_INSERT_ID();", connection);

                    cmd.Parameters.AddWithValue("@name", tour.TourName);
                    cmd.Parameters.AddWithValue("@code", tour.TourCode ?? "");
                    cmd.Parameters.AddWithValue("@price", tour.Price);
                    cmd.Parameters.AddWithValue("@img", tour.ImageUrl ?? "");
                    cmd.Parameters.AddWithValue("@location", tour.DepartureLocation ?? "");
                    cmd.Parameters.AddWithValue("@duration", tour.Duration ?? "");
                    cmd.Parameters.AddWithValue("@url", tour.TourDetailUrl ?? "");
                    cmd.Parameters.AddWithValue("@departureDates", departureDatesText);
                    cmd.Parameters.AddWithValue("@importantNotes", importantNotesText);

                    object? result = await cmd.ExecuteScalarAsync();
                    if (result != null && int.TryParse(result.ToString(), out int id))
                    {
                        insertedTourId = id;
                        Console.WriteLine($"✅ Đã insert tour '{tour.TourName}' với ID: {insertedTourId}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Không lấy được ID cho tour: {tour.TourName}");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Lỗi khi insert tour: {tour.TourName} - {ex.Message}");
                    continue;
                }

                // Insert schedules
                foreach (var scheduleItem in tour.Schedules ?? new List<TourScheduleItem>())
                {
                    if (string.IsNullOrWhiteSpace(scheduleItem.DayTitle) && string.IsNullOrWhiteSpace(scheduleItem.DayContent))
                        continue;

                    try
                    {
                        using var scheduleCmd = new MySqlCommand(@"
                            INSERT INTO schedules (tour_id, day_title, day_content)
                            VALUES (@id, @title, @content);", connection);

                        scheduleCmd.Parameters.AddWithValue("@id", insertedTourId);
                        scheduleCmd.Parameters.AddWithValue("@title", scheduleItem.DayTitle ?? "");
                        scheduleCmd.Parameters.AddWithValue("@content", scheduleItem.DayContent ?? "");

                        await scheduleCmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Lỗi insert schedule cho tour_id = {insertedTourId}: {ex.Message}");
                    }
                }
            }
        }
    }
}
