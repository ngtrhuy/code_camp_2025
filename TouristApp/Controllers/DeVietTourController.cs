using Fizzler.Systems.HtmlAgilityPack;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Newtonsoft.Json.Linq;
using System.Data;
using System.Text.RegularExpressions;
using TouristApp.Models;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeVietTourController : ControllerBase
    {
        private readonly DeVietTourCrawler _crawler;
        private readonly DeVietTourDataInserter _inserter;
        private readonly string _connectionString = "server=localhost;database=code_camp_2025;user=root;password=;";

        public DeVietTourController()
        {
            _crawler = new DeVietTourCrawler();
            _inserter = new DeVietTourDataInserter(_connectionString);
        }

        //string GetCleanText(HtmlNode node, string selector)
        //{
        //    var text = node.QuerySelector(selector)?.InnerText ?? "";
        //    return Regex.Replace(text, @"\s+", " ").Trim();
        //}

        //string GetAttr(HtmlNode node, string selector, string attr)
        //{
        //    return string.IsNullOrWhiteSpace(selector)
        //        ? ""
        //        : node.QuerySelector(selector)?.GetAttributeValue(attr, "") ?? "";
        //}

        [HttpGet("crawl")]
        public async Task<IActionResult> CrawlTours()
        {
            var urls = new List<string>
            {
                "https://deviet.vn/du-lich/tour-du-lich-chau-au-tron-goi/",
                "https://deviet.vn/gioi-thieu-tour-ghep-du-lich-chau-au/",
                "https://deviet.vn/du-lich/tour-nuoc-ngoai/"
            };

            var allTours = new List<StandardTourModel>();

            foreach (var url in urls)
            {
                var tours = await _crawler.CrawlToursAsync(url);
                allTours.AddRange(tours);
            }

            return Ok(allTours);
        }

        [HttpPost("crawl-and-insert")]
        public async Task<IActionResult> CrawlAndInsert()
        {
            var urls = new List<string>
            {
                "https://deviet.vn/du-lich/tour-du-lich-chau-au-tron-goi/",
                "https://deviet.vn/gioi-thieu-tour-ghep-du-lich-chau-au/",
                "https://deviet.vn/du-lich/tour-nuoc-ngoai/"
            };

            var allTours = new List<StandardTourModel>();

            foreach (var url in urls)
            {
                var tours = await _crawler.CrawlToursAsync(url);
                allTours.AddRange(tours);
            }

            await _inserter.InsertToursAsync(allTours);

            return Ok(new
            {
                Message = "Crawl và insert thành công",
                InsertedCount = allTours.Count
            });
        }

        [HttpGet("tours")]
        public IActionResult GetTours()
        {
            var tours = new List<object>();

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                var query = "SELECT * FROM tours";
                using (var command = new MySqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tours.Add(new
                        {
                            TourId = reader["id"],
                            TourName = reader["tour_name"],
                            TourCode = reader["tour_code"],
                            Price = reader["price"],
                            ImageUrl = reader["image_url"],
                            DepartureLocation = reader["departure_location"],
                            Duration = reader["duration"],
                            TourDetailUrl = reader["tour_detail_url"],
                            DepartureDates = reader["departure_dates"],
                            ImportantNotes = reader["important_notes"]
                        });
                    }
                }
            }

            return Ok(tours);
        }

        [HttpGet("schedules/{tourId}")]
        public IActionResult GetSchedulesByTourId(int tourId)
        {
            var schedules = new List<object>();

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();
                var query = "SELECT * FROM schedules WHERE tour_id = @tourId";
                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@tourId", tourId);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            schedules.Add(new
                            {
                                ScheduleId = reader["id"],
                                TourId = reader["tour_id"],
                                DayTitle = reader["day_title"],
                                DayContent = reader["day_content"]
                            });
                        }
                    }
                }
            }

            return Ok(schedules);
        }

        [HttpPut("tours/{id}")]
        public IActionResult UpdateTour(int id, [FromBody] StandardTourModel tour)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            var query = @"UPDATE tours SET 
                            tour_name = @TourName,
                            tour_code = @TourCode,
                            price = @Price,
                            image_url = @ImageUrl,
                            departure_location = @DepartureLocation,
                            duration = @Duration,
                            tour_detail_url = @TourDetailUrl,
                            departure_dates = @DepartureDates,
                            important_notes = @ImportantNotes
                          WHERE id = @Id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@TourName", tour.TourName);
            command.Parameters.AddWithValue("@TourCode", tour.TourCode);
            command.Parameters.AddWithValue("@Price", tour.Price);
            command.Parameters.AddWithValue("@ImageUrl", tour.ImageUrl ?? "");
            command.Parameters.AddWithValue("@DepartureLocation", tour.DepartureLocation);
            command.Parameters.AddWithValue("@Duration", tour.Duration);
            command.Parameters.AddWithValue("@TourDetailUrl", tour.TourDetailUrl ?? "");

            var departureDatesText = string.Join(",", tour.DepartureDates ?? new List<string>());
            var notes = (tour.ImportantNotes ?? new Dictionary<string, string>()).Select(kvp => $"{kvp.Key}:{kvp.Value}");
            var importantNotesText = string.Join(",", notes);

            command.Parameters.AddWithValue("@DepartureDates", departureDatesText);
            command.Parameters.AddWithValue("@ImportantNotes", importantNotesText);

            var rows = command.ExecuteNonQuery();
            return Ok(new { Message = "Cập nhật tour thành công", AffectedRows = rows });
        }

        [HttpPut("schedules/{id}")]
        public IActionResult UpdateSchedule(int id, [FromBody] TourScheduleItem schedule)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            var query = @"UPDATE schedules SET 
                            day_title = @DayTitle,
                            day_content = @DayContent
                          WHERE id = @Id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@DayTitle", schedule.DayTitle);
            command.Parameters.AddWithValue("@DayContent", schedule.DayContent);

            var rows = command.ExecuteNonQuery();
            return Ok(new { Message = "Cập nhật lịch trình thành công", AffectedRows = rows });
        }

        [HttpDelete("tours/{id}")]
        public IActionResult DeleteTourAndSchedules(int id)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var deleteSchedules = new MySqlCommand("DELETE FROM schedules WHERE tour_id = @Id", connection, transaction);
                deleteSchedules.Parameters.AddWithValue("@Id", id);
                deleteSchedules.ExecuteNonQuery();

                var deleteTour = new MySqlCommand("DELETE FROM tours WHERE id = @Id", connection, transaction);
                deleteTour.Parameters.AddWithValue("@Id", id);
                var affectedRows = deleteTour.ExecuteNonQuery();

                transaction.Commit();
                return Ok(new { Message = "Xóa tour và lịch trình thành công", AffectedRows = affectedRows });
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                return StatusCode(500, new { Message = "Lỗi khi xóa", Error = ex.Message });
            }
        }

    }
}
