using Microsoft.AspNetCore.Mvc;
using TouristApp.Models;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [Route("api/crawl")]
    [ApiController]
    public class GenericCrawlController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly string _connectionString;
        private readonly TourRepository _tourRepository;
        private readonly IHistoryRepository _history;

        public GenericCrawlController(IConfiguration configuration, IHistoryRepository history)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _tourRepository = new TourRepository(configuration); // Khởi tạo repository có sẵn của bạn
            _history = history;
        }

        // ✅ Crawl và lưu DB (không dùng history)
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

        // ✅ Crawl không lưu DB (không dùng history)
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

        // ✅ Load config từ DB
        private PageConfigModel? GetConfigById(int id)
        {
            PageConfigModel? config = null;
            using (var conn = new MySql.Data.MySqlClient.MySqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = new MySql.Data.MySqlClient.MySqlCommand("SELECT * FROM page_config WHERE id = @Id", conn);
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

        // ✅ Generic crawl (không dùng history)
        [HttpGet("{configId}")]
        public async Task<IActionResult> Crawl(int configId)
        {
            try
            {
                var tempService = new GenericCrawlServiceServerSide(); // chỉ để load config
                var config = await tempService.LoadPageConfig(configId);
                if (config == null) return NotFound("Không tìm thấy cấu hình crawl.");

                var crawlService = CrawlServiceFactory.CreateCrawlService(config);
                var data = await crawlService.CrawlFromPageConfigAsync(configId);

                if (data.Count == 0) return NotFound("Không tìm thấy hoặc không crawl được dữ liệu.");

                return Ok(new
                {
                    message = $"Crawl thành công {data.Count} tour từ {config.BaseDomain}",
                    crawl_type = config.CrawlType,
                    data
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi crawl: {ex.Message}" });
            }
        }

        // ✅ Crawl server-side có HISTORY: pending → done/failed (trả về historyId để FE poll)
        [HttpGet("server-side/{configId}")]
        public async Task<IActionResult> CrawlServerSide(int configId)
        {
            // 1) tạo history pending
            var historyId = await _history.CreateAsync(configId, "pending", "Starting crawl...");

            // 2) chạy nền để không block FE
            _ = Task.Run(async () =>
            {
                try
                {
                    var service = new GenericCrawlServiceServerSide();
                    var data = await service.CrawlFromPageConfigAsync(configId);
                    await _history.UpdateAsync(historyId, "done", $"Crawled {data.Count} tours successfully.");
                }
                catch (Exception ex)
                {
                    await _history.UpdateAsync(historyId, "failed", ex.ToString());
                }
            });

            // 3) trả về ngay để FE mở modal & poll
            return Ok(new { message = "Crawl started", historyId });
        }

        // ✅ Crawl client-side có HISTORY (nếu bạn cần)
        [HttpGet("client-side/{configId}")]
        public async Task<IActionResult> CrawlClientSide(int configId)
        {
            var historyId = await _history.CreateAsync(configId, "pending", "Starting crawl...");
            _ = Task.Run(async () =>
            {
                try
                {
                    var service = new GenericCrawlServiceClientSide();
                    var data = await service.CrawlFromPageConfigAsync(configId);
                    await _history.UpdateAsync(historyId, "done", $"Crawled {data.Count} tours successfully.");
                }
                catch (Exception ex)
                {
                    await _history.UpdateAsync(historyId, "failed", ex.ToString());
                }
            });
            return Ok(new { message = "Crawl started", historyId });
        }
    }
}
