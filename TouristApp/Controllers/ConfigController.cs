using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
using TouristApp.Models;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;

        public ConfigController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
        }

        // GET: api/config/pageconfigs
        [HttpGet("pageconfigs")]
        public IActionResult GetPageConfigs([FromQuery] string? crawlType)
        {
            var configs = new List<object>();

            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();

                string query = "SELECT * FROM page_config";
                if (!string.IsNullOrEmpty(crawlType))
                {
                    query += " WHERE crawl_type = @CrawlType";
                }

                using (var command = new MySqlCommand(query, connection))
                {
                    if (!string.IsNullOrEmpty(crawlType))
                    {
                        command.Parameters.AddWithValue("@CrawlType", crawlType);
                    }

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            configs.Add(new
                            {
                                Id = reader["id"],
                                BaseDomain = reader["base_domain"],
                                BaseUrl = reader["base_url"],
                                TourName = reader["tour_name"],
                                TourCode = reader["tour_code"],
                                TourPrice = reader["tour_price"],
                                ImageUrl = reader["image_url"],
                                DepartureLocation = reader["departure_location"],
                                DepartureDate = reader["departure_date"],
                                TourDuration = reader["tour_duration"],
                                TourDetailUrl = reader["tour_detail_url"],
                                TourDetailDayTitle = reader["tour_detail_day_title"],
                                TourDetailDayContent = reader["tour_detail_day_content"],
                                TourDetailNote = reader["tour_detail_note"],
                                CrawlType = reader["crawl_type"]
                            });
                        }
                    }
                }
            }

            return Ok(configs);
        }


        // POST: api/config/pageconfigs
        [HttpPost("pageconfigs")]
        public IActionResult CreatePageConfig([FromBody] PageConfigModel config)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            var query = @"
        INSERT INTO page_config (
            base_domain, base_url, tour_name, tour_code, tour_price, image_url, 
            departure_location, departure_date, tour_duration, tour_detail_url, 
            tour_detail_day_title, tour_detail_day_content, tour_detail_note, crawl_type
        ) VALUES (
            @BaseDomain, @BaseUrl, @TourName, @TourCode, @TourPrice, @ImageUrl, 
            @DepartureLocation, @DepartureDate, @TourDuration, @TourDetailUrl, 
            @TourDetailDayTitle, @TourDetailDayContent, @TourDetailNote, @CrawlType
        );";

            using var command = new MySqlCommand(query, connection);

            command.Parameters.AddWithValue("@BaseDomain", config.BaseDomain);
            command.Parameters.AddWithValue("@BaseUrl", config.BaseUrl);
            command.Parameters.AddWithValue("@TourName", config.TourName);
            command.Parameters.AddWithValue("@TourCode", config.TourCode);
            command.Parameters.AddWithValue("@TourPrice", config.TourPrice);
            command.Parameters.AddWithValue("@ImageUrl", config.ImageUrl);
            command.Parameters.AddWithValue("@DepartureLocation", config.DepartureLocation);
            command.Parameters.AddWithValue("@DepartureDate", config.DepartureDate);
            command.Parameters.AddWithValue("@TourDuration", config.TourDuration);
            command.Parameters.AddWithValue("@TourDetailUrl", config.TourDetailUrl);
            command.Parameters.AddWithValue("@TourDetailDayTitle", config.TourDetailDayTitle);
            command.Parameters.AddWithValue("@TourDetailDayContent", config.TourDetailDayContent);
            command.Parameters.AddWithValue("@TourDetailNote", config.TourDetailNote);
            command.Parameters.AddWithValue("@CrawlType", config.CrawlType);

            command.ExecuteNonQuery();

            return Ok(new { message = "Created successfully" });
        }


        [HttpPut("pageconfigs/{id}")]
        public IActionResult UpdatePageConfig(int id, [FromBody] PageConfigModel config)
        {
            using (var connection = new MySqlConnection(_connectionString))
            {
                connection.Open();

                var query = @"
            UPDATE page_config SET 
                base_domain = @BaseDomain,
                base_url = @BaseUrl,
                tour_name = @TourName,
                tour_code = @TourCode,
                tour_price = @TourPrice,
                image_url = @ImageUrl,
                departure_location = @DepartureLocation,
                departure_date = @DepartureDate,
                tour_duration = @TourDuration,
                tour_detail_url = @TourDetailUrl,
                tour_detail_day_title = @TourDetailDayTitle,
                tour_detail_day_content = @TourDetailDayContent,
                tour_detail_note = @TourDetailNote,
                crawl_type = @CrawlType
            WHERE id = @Id";

                using (var command = new MySqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.Parameters.AddWithValue("@BaseDomain", config.BaseDomain);
                    command.Parameters.AddWithValue("@BaseUrl", config.BaseUrl);
                    command.Parameters.AddWithValue("@TourName", config.TourName);
                    command.Parameters.AddWithValue("@TourCode", config.TourCode);
                    command.Parameters.AddWithValue("@TourPrice", config.TourPrice);
                    command.Parameters.AddWithValue("@ImageUrl", config.ImageUrl);
                    command.Parameters.AddWithValue("@DepartureLocation", config.DepartureLocation);
                    command.Parameters.AddWithValue("@DepartureDate", config.DepartureDate);
                    command.Parameters.AddWithValue("@TourDuration", config.TourDuration);
                    command.Parameters.AddWithValue("@TourDetailUrl", config.TourDetailUrl);
                    command.Parameters.AddWithValue("@TourDetailDayTitle", config.TourDetailDayTitle);
                    command.Parameters.AddWithValue("@TourDetailDayContent", config.TourDetailDayContent);
                    command.Parameters.AddWithValue("@TourDetailNote", config.TourDetailNote);
                    command.Parameters.AddWithValue("@CrawlType", config.CrawlType);

                    command.ExecuteNonQuery();
                }
            }

            return Ok(new { message = "Cập nhật cấu hình thành công!" });
        }


        // DELETE: api/config/pageconfigs/{id}
        [HttpDelete("pageconfigs/{id}")]
        public IActionResult DeletePageConfig(int id)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            var query = "DELETE FROM page_config WHERE id = @Id";

            using var command = new MySqlCommand(query, connection);
            command.Parameters.AddWithValue("@Id", id);

            command.ExecuteNonQuery();

            return Ok(new { message = "Deleted successfully" });
        }
    }
}
