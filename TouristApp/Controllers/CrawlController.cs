using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using TouristApp.Models;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CrawlController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly TourRepository _tourRepository;

        public CrawlController(IConfiguration configuration)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _tourRepository = new TourRepository(configuration); // Khởi tạo repository
        }

        /// <summary>
        /// Crawl và lưu DB
        /// </summary>
        [HttpGet("crawl-and-save/{id}")]
        public IActionResult CrawlAndSave(int id)
        {
            var config = GetConfigById(id);
            if (config == null) return NotFound("❌ Không tìm thấy cấu hình crawl");

            var service = new SeleniumCrawlService();
            var tours = service.CrawlToursWithSelenium(config);
            int savedCount = _tourRepository.SaveTours(tours);

            return Ok(new
            {
                message = "✅ Đã crawl và lưu vào DB",
                totalCrawled = tours.Count,
                totalSaved = savedCount
            });
        }

        /// <summary>
        /// Crawl chỉ hiển thị dữ liệu, không lưu DB
        /// </summary>
        [HttpGet("crawl-only/{id}")]
        public IActionResult GetToursOnly(int id)
        {
            var config = GetConfigById(id);
            if (config == null) return NotFound("❌ Không tìm thấy cấu hình crawl");

            var service = new SeleniumCrawlService();
            var tours = service.CrawlToursWithSelenium(config);

            return Ok(new
            {
                message = "✅ Đã crawl dữ liệu thành công",
                totalCrawled = tours.Count,
                tours
            });
        }

        /// <summary>
        /// Hàm private để lấy config
        /// </summary>
        private PageConfigModel? GetConfigById(int id)
        {
            PageConfigModel? config = null;
            using (var conn = new MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = new MySqlCommand("SELECT * FROM page_config WHERE id = @Id", conn);
                cmd.Parameters.AddWithValue("@Id", id);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    config = new PageConfigModel
                    {
                        Id = Convert.ToInt32(reader["id"]),
                        BaseDomain = reader["base_domain"].ToString()!,
                        BaseUrl = reader["base_url"].ToString()!,
                        TourName = reader["tour_name"].ToString()!,
                        TourCode = reader["tour_code"].ToString()!,
                        TourPrice = reader["tour_price"].ToString()!,
                        ImageUrl = reader["image_url"].ToString()!,
                        DepartureLocation = reader["departure_location"].ToString()!,
                        DepartureDate = reader["departure_date"].ToString()!,
                        TourDuration = reader["tour_duration"].ToString()!,
                        PagingType = reader["paging_type"].ToString()!,
                        TourDetailUrl = reader["tour_detail_url"].ToString()!,
                        TourDetailDayTitle = reader["tour_detail_day_title"].ToString()!,
                        TourDetailDayContent = reader["tour_detail_day_content"].ToString()!,
                        TourDetailNote = reader["tour_detail_note"].ToString()!,
                        CrawlType = reader["crawl_type"].ToString()!,
                        TourListSelector = reader["tour_list_selector"].ToString()!,
                        ImageAttr = reader["image_attr"].ToString()!,
                        TourDetailAttr = reader["tour_detail_attr"].ToString()!,
                        LoadMoreButtonSelector = reader["load_more_button_selector"].ToString()!,
                        LoadMoreType = reader["load_more_type"].ToString()!
                    };
                }
            }
            return config;
        }
    }
}
