using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
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

        // folder lưu ảnh tĩnh
        private readonly string _imageFolder;
        private static readonly HttpClient _http = new HttpClient();

        public GenericCrawlController(IConfiguration configuration, IHistoryRepository history)
        {
            _configuration = configuration;
            _connectionString = _configuration.GetConnectionString("DefaultConnection");
            _tourRepository = new TourRepository(configuration);
            _history = history;

            _imageFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images");
            if (!Directory.Exists(_imageFolder))
                Directory.CreateDirectory(_imageFolder);

            // user-agent để tải ảnh từ 1 số site khó tính
            if (!_http.DefaultRequestHeaders.UserAgent.Any())
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }
        /*
                // ✅ Crawl và lưu DB (không dùng history) — giữ nguyên
                [HttpGet("crawl-and-save/{id}")]
                public IActionResult CrawlAndSave(int id, [FromQuery] int limit)
                {
                    if (limit <= 0) return BadRequest("limit phải > 0");

                    var config = GetConfigById(id);
                    if (config == null) return NotFound("❌ Không tìm thấy cấu hình crawl");

                    // Đã sửa ở đây
                    var service = new SeleniumCrawlService(_tourRepository);
                    var tours = service.CrawlToursWithSelenium(config, limit);
                    int savedCount = _tourRepository.SaveTours(tours);

                    return Ok(new
                    {
                        message = "✅ Đã crawl và lưu vào DB",
                        requested = limit,
                        totalCrawled = tours.Count,
                        totalSaved = savedCount
                    });
                }


                // ✅ Crawl không lưu DB, cho phép nhập số lượng tour qua ?limit=
                // GET /api/crawl/crawl-only/1?limit=10
                [HttpGet("crawl-only/{id}")]
                public IActionResult GetToursOnly(int id, [FromQuery] int limit)
                {
                    if (limit <= 0) return BadRequest("limit phải > 0");

                    var config = GetConfigById(id);
                    if (config == null) return NotFound("❌ Không tìm thấy cấu hình crawl");

                    // Đã sửa ở đây
                    var service = new SeleniumCrawlService(_tourRepository);
                    var tours = service.CrawlToursWithSelenium(config, limit);

                    return Ok(new
                    {
                        message = "✅ Đã crawl dữ liệu thành công",
                        requested = limit,
                        totalCrawled = tours.Count,
                        tours
                    });
                }
        */

        // ✅ Load config từ DB — giữ nguyên
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

        // ✅ Generic crawl (sync) — thêm bước rewrite ảnh trước khi trả
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

                await RewriteImagesToLocal(data, config.BaseDomain);

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

        // ✅ Crawl server-side có HISTORY — thêm bước lưu ảnh + (tuỳ chọn) lưu DB
        [HttpGet("server-side/{configId}")]
        public async Task<IActionResult> CrawlServerSide(int configId)
        {
            var historyId = await _history.CreateAsync(configId, "pending", "Starting crawl...");

            _ = Task.Run(async () =>
            {
                try
                {
                    var service = new GenericCrawlServiceServerSide();
                    var data = await service.CrawlFromPageConfigAsync(configId);

                    // tải ảnh về máy + thay URL
                    var cfg = await service.LoadPageConfig(configId);
                    var baseDomain = cfg?.BaseDomain ?? "";
                    await RewriteImagesToLocal(data, baseDomain);

                    // (tuỳ chọn) lưu DB — nếu bạn muốn gắn vào pipeline crawl
                    _tourRepository.SaveTours(data);

                    await _history.UpdateAsync(historyId, "done", $"Crawled {data.Count} tours successfully.");
                }
                catch (Exception ex)
                {
                    await _history.UpdateAsync(historyId, "failed", ex.ToString());
                }
            });

            return Ok(new { message = "Crawl started", historyId });
        }


        [HttpGet("client-side/{configId}")]
        public async Task<IActionResult> CrawlClientSide(int configId, [FromQuery] int limit = 20)
        {
            var historyId = await _history.CreateAsync(configId, "pending", "Starting crawl...");

            _ = Task.Run(async () =>
            {
                try
                {
                    var service = new GenericCrawlServiceClientSide();
                    var data = await service.CrawlFromPageConfigAsync(configId, limit);

                    // tải ảnh về máy + thay URL
                    var cfg = await service.LoadPageConfig(configId);
                    var baseDomain = cfg?.BaseDomain ?? "";
                    await RewriteImagesToLocal(data, baseDomain);

                    // (tuỳ chọn) lưu DB
                    _tourRepository.SaveTours(data);

                    await _history.UpdateAsync(historyId, "done", $"Crawled {data.Count} tours successfully.");
                }
                catch (Exception ex)
                {
                    await _history.UpdateAsync(historyId, "failed", ex.ToString());
                }
            });

            return Ok(new { message = "Crawl started", historyId });
        }

        // ================== Helpers: tải ảnh & ghi đè URL ==================
        private async Task RewriteImagesToLocal(List<StandardTourModel> tours, string baseDomain)
        {
            foreach (var t in tours)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(t.ImageUrl)) continue;

                    var absolute = ToAbsoluteUrl(baseDomain, t.ImageUrl);
                    var localUrl = await SaveImageAsync(absolute); // => "/images/<file>"
                    if (!string.IsNullOrEmpty(localUrl))
                        t.ImageUrl = localUrl;
                }
                catch { /* skip từng ảnh lỗi */ }
            }
        }

        private static string ToAbsoluteUrl(string baseDomain, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return url;
            if (string.IsNullOrWhiteSpace(baseDomain)) return url;
            return $"{baseDomain.TrimEnd('/')}/{url.TrimStart('/')}";
        }

        private async Task<string> SaveImageAsync(string remoteUrl)
        {
            try
            {
                using var res = await _http.GetAsync(remoteUrl);
                if (!res.IsSuccessStatusCode) return "";

                var bytes = await res.Content.ReadAsByteArrayAsync();

                var ext = GetExtension(remoteUrl, res.Content.Headers);
                var file = $"{Guid.NewGuid():N}{ext}";
                var full = Path.Combine(_imageFolder, file);
                await System.IO.File.WriteAllBytesAsync(full, bytes);

                // trả về URL tĩnh để FE dùng
                return $"/images/{file}";
            }
            catch
            {
                return "";
            }
        }

        private static string GetExtension(string url, HttpContentHeaders headers)
        {
            // thử từ URL
            try
            {
                var path = new Uri(url).AbsolutePath;
                var ext = Path.GetExtension(path);
                if (!string.IsNullOrWhiteSpace(ext)) return ext;
            }
            catch { /* ignore */ }

            // thử từ content-type
            var ct = headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (ct.Contains("png")) return ".png";
            if (ct.Contains("webp")) return ".webp";
            if (ct.Contains("gif")) return ".gif";
            if (ct.Contains("jpeg") || ct.Contains("jpg")) return ".jpg";

            return ".jpg";
        }
    }
}
