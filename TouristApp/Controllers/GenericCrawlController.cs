using Microsoft.AspNetCore.Mvc;
using TouristApp.Models;
using TouristApp.Services;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;

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

        // folders
        private readonly string _imageFolder;
        private readonly string _previewFolder;

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

            _previewFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "previews");
            if (!Directory.Exists(_previewFolder))
                Directory.CreateDirectory(_previewFolder);

            if (!_http.DefaultRequestHeaders.UserAgent.Any())
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }
        // ================== Generic crawl sync quick ==================
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

        // ================== Crawl nền có HISTORY: CHỈ LƯU PREVIEW ==================
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

                    var cfg = await service.LoadPageConfig(configId);
                    var baseDomain = cfg?.BaseDomain ?? "";
                    await RewriteImagesToLocal(data, baseDomain);

                    // ❗ lưu preview – chưa ghi DB
                    await SavePreviewAsync(historyId, data);

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

                    // ❗ lưu preview – chưa ghi DB
                    await SavePreviewAsync(historyId, data);

                    await _history.UpdateAsync(historyId, "done", $"Crawled {data.Count} tours successfully.");
                }
                catch (Exception ex)
                {
                    await _history.UpdateAsync(historyId, "failed", ex.ToString());
                }
            });

            return Ok(new { message = "Crawl started", historyId });
        }

        // ================== PREVIEW APIs ==================
        // Trả preview theo snake_case fields như yêu cầu
        [HttpGet("preview/{historyId:long}")]
        public IActionResult GetPreview(long historyId)
        {
            var data = LoadPreview(historyId);
            var mapped = data.Select((t, idx) => new Dictionary<string, object?>
            {
                ["id"] = idx + 1,
                ["tour_name"] = t.TourName,
                ["tour_code"] = t.TourCode,
                ["price"] = t.Price,
                ["image_url"] = t.ImageUrl,
                ["departure_location"] = t.DepartureLocation,
                ["duration"] = t.Duration,
                ["tour_detail_url"] = t.TourDetailUrl,
                ["departure_dates"] = t.DepartureDates,
                ["important_notes"] = t.ImportantNotes,
                ["source_site"] = t.SourceSite,
                // ➕ LỊCH TRÌNH (list các object có day_title/day_content)
                ["schedule"] = t.Schedule?.Select(d => new {
                    day_title = d.DayTitle,
                    day_content = d.DayContent
                }).ToList()
            }).ToList();

            return Ok(new { total = mapped.Count, tours = mapped });
        }

        // Nhập DB từ preview
        [HttpPost("import/{historyId:long}")]
        public IActionResult ImportFromPreview(long historyId)
        {
            var data = LoadPreview(historyId);
            if (data.Count == 0) return NotFound(new { message = "Không có dữ liệu preview để import." });

            var saved = _tourRepository.SaveTours(data);

            // Xoá file preview sau khi import (tùy bạn)
            var path = Path.Combine(_previewFolder, $"{historyId}.json");
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

            return Ok(new { message = "Đã import vào DB", total = data.Count, saved });
        }

        // ================== Load Config từ DB ==================
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

        // ================== Helpers: Preview ==================
        private async Task SavePreviewAsync(long historyId, List<StandardTourModel> tours)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            };
            var path = Path.Combine(_previewFolder, $"{historyId}.json");
            using var fs = System.IO.File.Create(path);
            await JsonSerializer.SerializeAsync(fs, tours, jsonOptions);
        }

        private List<StandardTourModel> LoadPreview(long historyId)
        {
            var path = Path.Combine(_previewFolder, $"{historyId}.json");
            if (!System.IO.File.Exists(path)) return new List<StandardTourModel>();
            var json = System.IO.File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<StandardTourModel>>(json) ?? new List<StandardTourModel>();
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
                    var localUrl = await SaveImageAsync(absolute);
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

                return $"/images/{file}";
            }
            catch
            {
                return "";
            }
        }

        private static string GetExtension(string url, HttpContentHeaders headers)
        {
            try
            {
                var path = new Uri(url).AbsolutePath;
                var ext = Path.GetExtension(path);
                if (!string.IsNullOrWhiteSpace(ext)) return ext;
            }
            catch { /* ignore */ }

            var ct = headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
            if (ct.Contains("png")) return ".png";
            if (ct.Contains("webp")) return ".webp";
            if (ct.Contains("gif")) return ".gif";
            if (ct.Contains("jpeg") || ct.Contains("jpg")) return ".jpg";

            return ".jpg";
        }
    }
}
