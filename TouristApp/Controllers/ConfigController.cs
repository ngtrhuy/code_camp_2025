// Controllers/ConfigController.cs
using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
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

        // nhỏ gọn: helper set DBNull cho null/empty
        private static void AddParam(MySqlCommand cmd, string name, object? value)
        {
            cmd.Parameters.AddWithValue(name, value is null ? DBNull.Value :
                                              value is string s && string.IsNullOrWhiteSpace(s) ? DBNull.Value :
                                              value);
        }

        [HttpGet("pageconfigs")]
        public IActionResult GetPageConfigs([FromQuery] string? crawlType)
        {
            var configs = new List<object>();
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            var sql = "SELECT * FROM page_config";
            if (!string.IsNullOrWhiteSpace(crawlType)) sql += " WHERE crawl_type = @CrawlType";

            using var cmd = new MySqlCommand(sql, connection);
            if (!string.IsNullOrWhiteSpace(crawlType)) AddParam(cmd, "@CrawlType", crawlType);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                configs.Add(new
                {
                    Id = reader["id"],
                    BaseDomain = reader["base_domain"],
                    BaseUrl = reader["base_url"],
                    TourListSelector = reader["tour_list_selector"],
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
                    CrawlType = reader["crawl_type"],
                    ImageAttr = reader["image_attr"],
                    TourDetailAttr = reader["tour_detail_attr"],
                    LoadMoreButtonSelector = reader["load_more_button_selector"],
                    LoadMoreType = reader["load_more_type"],
                    PagingType = reader["paging_type"],
                });
            }

            return Ok(configs);
        }

        [HttpPost("pageconfigs")]
        public IActionResult CreatePageConfig([FromBody] PageConfigModel config)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                connection.Open();

                const string sql = @"
INSERT INTO page_config (
  base_domain, base_url, tour_list_selector, tour_name, tour_code, tour_price, image_url,
  departure_location, departure_date, tour_duration, tour_detail_url,
  tour_detail_day_title, tour_detail_day_content, tour_detail_note, crawl_type,
  image_attr, tour_detail_attr, load_more_button_selector, load_more_type, paging_type
) VALUES (
  @BaseDomain, @BaseUrl, @TourListSelector, @TourName, @TourCode, @TourPrice, @ImageUrl,
  @DepartureLocation, @DepartureDate, @TourDuration, @TourDetailUrl,
  @TourDetailDayTitle, @TourDetailDayContent, @TourDetailNote, @CrawlType,
  @ImageAttr, @TourDetailAttr, @LoadMoreButtonSelector, @LoadMoreType, @PagingType
);";

                using var cmd = new MySqlCommand(sql, connection);
                AddParam(cmd, "@BaseDomain", config.BaseDomain);
                AddParam(cmd, "@BaseUrl", config.BaseUrl);
                AddParam(cmd, "@TourListSelector", config.TourListSelector);
                AddParam(cmd, "@TourName", config.TourName);
                AddParam(cmd, "@TourCode", config.TourCode);
                AddParam(cmd, "@TourPrice", config.TourPrice);
                AddParam(cmd, "@ImageUrl", config.ImageUrl);
                AddParam(cmd, "@DepartureLocation", config.DepartureLocation);
                AddParam(cmd, "@DepartureDate", config.DepartureDate);
                AddParam(cmd, "@TourDuration", config.TourDuration);
                AddParam(cmd, "@TourDetailUrl", config.TourDetailUrl);
                AddParam(cmd, "@TourDetailDayTitle", config.TourDetailDayTitle);
                AddParam(cmd, "@TourDetailDayContent", config.TourDetailDayContent);
                AddParam(cmd, "@TourDetailNote", config.TourDetailNote);
                AddParam(cmd, "@CrawlType", config.CrawlType);
                AddParam(cmd, "@ImageAttr", config.ImageAttr);
                AddParam(cmd, "@TourDetailAttr", config.TourDetailAttr);
                AddParam(cmd, "@LoadMoreButtonSelector", config.LoadMoreButtonSelector);
                AddParam(cmd, "@LoadMoreType", config.LoadMoreType);
                AddParam(cmd, "@PagingType", config.PagingType);

                cmd.ExecuteNonQuery();
                return Ok(new { message = "Created successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Create failed", error = ex.Message });
            }
        }

        [HttpPut("pageconfigs/{id}")]
        public IActionResult UpdatePageConfig(int id, [FromBody] PageConfigModel config)
        {
            try
            {
                using var connection = new MySqlConnection(_connectionString);
                connection.Open();

                const string sql = @"
UPDATE page_config SET
  base_domain = @BaseDomain,
  base_url = @BaseUrl,
  tour_list_selector = @TourListSelector,
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
  crawl_type = @CrawlType,
  image_attr = @ImageAttr,
  tour_detail_attr = @TourDetailAttr,
  load_more_button_selector = @LoadMoreButtonSelector,
  load_more_type = @LoadMoreType,
  paging_type = @PagingType
WHERE id = @Id;";

                using var cmd = new MySqlCommand(sql, connection);
                AddParam(cmd, "@Id", id);
                AddParam(cmd, "@BaseDomain", config.BaseDomain);
                AddParam(cmd, "@BaseUrl", config.BaseUrl);
                AddParam(cmd, "@TourListSelector", config.TourListSelector);
                AddParam(cmd, "@TourName", config.TourName);
                AddParam(cmd, "@TourCode", config.TourCode);
                AddParam(cmd, "@TourPrice", config.TourPrice);
                AddParam(cmd, "@ImageUrl", config.ImageUrl);
                AddParam(cmd, "@DepartureLocation", config.DepartureLocation);
                AddParam(cmd, "@DepartureDate", config.DepartureDate);
                AddParam(cmd, "@TourDuration", config.TourDuration);
                AddParam(cmd, "@TourDetailUrl", config.TourDetailUrl);
                AddParam(cmd, "@TourDetailDayTitle", config.TourDetailDayTitle);
                AddParam(cmd, "@TourDetailDayContent", config.TourDetailDayContent);
                AddParam(cmd, "@TourDetailNote", config.TourDetailNote);
                AddParam(cmd, "@CrawlType", config.CrawlType);
                // 🔧 BỔ SUNG 2 THAM SỐ BỊ THIẾU
                AddParam(cmd, "@ImageAttr", config.ImageAttr);
                AddParam(cmd, "@TourDetailAttr", config.TourDetailAttr);
                AddParam(cmd, "@LoadMoreButtonSelector", config.LoadMoreButtonSelector);
                AddParam(cmd, "@LoadMoreType", config.LoadMoreType);
                AddParam(cmd, "@PagingType", config.PagingType);

                var affected = cmd.ExecuteNonQuery();
                return Ok(new { message = "Cập nhật cấu hình thành công!", affected });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = "Update failed", error = ex.Message });
            }
        }

        [HttpDelete("pageconfigs/{id}")]
        public IActionResult DeletePageConfig(int id)
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();

            const string sql = "DELETE FROM page_config WHERE id = @Id";
            using var cmd = new MySqlCommand(sql, connection);
            AddParam(cmd, "@Id", id);
            var affected = cmd.ExecuteNonQuery();

            return Ok(new { message = "Deleted successfully", affected });
        }
    }
}
