using HtmlAgilityPack;
using MySql.Data.MySqlClient;
using MySqlConnector;
using TouristApp.Models;
using TouristApp.Services;
using MySqlCommand = MySqlConnector.MySqlCommand;
using MySqlConnection = MySqlConnector.MySqlConnection;

namespace TouristApp.Services
{
    public class PystravelCrawlService
    {
        private readonly HttpClient _httpClient;

        public PystravelCrawlService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Crawl danh sách tour từ trang chủ pystravel
        /// </summary>
        public async Task<List<StandardTourModel>> GetToursAsync()
        {
            var tours = new List<StandardTourModel>();
            var html = await _httpClient.GetStringAsync("https://pystravel.vn/");
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var tourNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'drop-shadow-md')]");
            if (tourNodes == null) return tours;

            foreach (var node in tourNodes)
            {
                try
                {
                    var tour = new StandardTourModel
                    {
                        TourName = node.SelectSingleNode(".//h3")?.InnerText?.Trim() ?? "",
                        ImageUrl = node.SelectSingleNode(".//img[contains(@class,'object-cover')]")?.GetAttributeValue("src", "") ?? "",
                        TourDetailUrl = node.SelectSingleNode(".//a")?.GetAttributeValue("href", "") ?? "",
                        Price = node.SelectSingleNode(".//div[contains(@class,'text-xl') and contains(@class,'font-bold')]")?.InnerText?.Trim() ?? "",
                        Duration = node.SelectSingleNode(".//div[contains(@class,'flex-1')]//span[contains(@class,'uppercase')]")?.InnerText?.Trim() ?? "",
                        DepartureLocation = node.SelectSingleNode(".//span[contains(text(),'Điểm đi:')]")?.InnerText?.Replace("Điểm đi:", "").Trim() ?? ""
                    };

                    // Chuẩn hóa đường dẫn
                    if (!string.IsNullOrEmpty(tour.ImageUrl) && !tour.ImageUrl.StartsWith("http"))
                        tour.ImageUrl = "https://pystravel.vn" + tour.ImageUrl;

                    if (!string.IsNullOrEmpty(tour.TourDetailUrl) && !tour.TourDetailUrl.StartsWith("http"))
                        tour.TourDetailUrl = "https://pystravel.vn" + tour.TourDetailUrl;

                    // Crawl chi tiết lịch trình, ngày khởi hành, ghi chú
                    await CrawlTourDetailAsync(tour);

                    tours.Add(tour);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Lỗi tour: {ex.Message}");
                }
            }

            return tours;
        }

        /// <summary>
        /// Gọi từ Controller để crawl + insert DB
        /// </summary>
        public async Task GetToursAsync(bool insertToDb)
        {
            var tours = await GetToursAsync();
            if (insertToDb && tours.Count > 0)
            {
                await InsertToursToDatabase(tours);
            }
        }

        private async Task CrawlTourDetailAsync(StandardTourModel tour)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(tour.TourDetailUrl);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // 📅 Ngày khởi hành
                var rows = doc.DocumentNode.SelectNodes("//tbody//tr");
                if (rows != null && rows.Count > 1)
                {
                    for (int i = 1; i < rows.Count; i++)
                    {
                        var cells = rows[i].SelectNodes(".//td");
                        if (cells != null && cells.Count >= 1)
                        {
                            var date = HtmlEntity.DeEntitize(cells[0].InnerText.Trim());
                            if (!string.IsNullOrEmpty(date))
                                tour.DepartureDates.Add(date);
                        }
                    }
                }

                // 📘 Lịch trình
                var scheduleBlocks = doc.DocumentNode.SelectNodes("//div[@id='plan']//div[contains(@class,'border-primary-v2')]");
                if (scheduleBlocks != null)
                {
                    foreach (var block in scheduleBlocks)
                    {
                        var title = HtmlEntity.DeEntitize(block.SelectSingleNode(".//h3")?.InnerText?.Trim() ?? "");
                        var content = HtmlEntity.DeEntitize(block.SelectSingleNode(".//div[contains(@class,'tour-detail_tour-content')]")?.InnerText?.Trim() ?? "");

                        if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(content))
                        {
                            tour.Schedule.Add(new TourScheduleItem
                            {
                                DayTitle = title,
                                DayContent = content
                            });
                        }
                    }
                }

                // 📌 Điều khoản
                var noteBlocks = doc.DocumentNode.SelectNodes("//div[@id='includes']//div[contains(@class,'border') and contains(@class,'p-6')]");
                if (noteBlocks != null)
                {
                    foreach (var section in noteBlocks)
                    {
                        var title = HtmlEntity.DeEntitize(section.SelectSingleNode(".//h3")?.InnerText?.Trim() ?? "");
                        var items = section.SelectNodes(".//ul/li")?.Select(li => "• " + HtmlEntity.DeEntitize(li.InnerText.Trim())).ToList();

                        if (!string.IsNullOrEmpty(title) && items != null && items.Any())
                        {
                            tour.ImportantNotes[title] = string.Join("\n", items);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi crawl chi tiết: {ex.Message}");
            }
        }

        private async Task InsertToursToDatabase(List<StandardTourModel> tours)
        {
            string connStr = "server=localhost;database=code_camp_2025;uid=root;pwd=;charset=utf8mb4;";


            using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();

            foreach (var tour in tours)
            {
                try
                {
                    // INSERT vào bảng tours
                    var cmd = new MySqlCommand(@"
                        INSERT INTO tours (
                            tour_name, tour_code, price, image_url,
                            departure_location, duration, tour_detail_url,
                            departure_dates, important_notes
                        )
                        VALUES (
                            @name, @code, @price, @img,
                            @location, @duration, @url,
                            @dates, @notes
                        );
                        SELECT LAST_INSERT_ID();
                    ", conn);

                    cmd.Parameters.AddWithValue("@name", tour.TourName);
                    cmd.Parameters.AddWithValue("@code", tour.TourCode);
                    cmd.Parameters.AddWithValue("@price", tour.Price);
                    cmd.Parameters.AddWithValue("@img", tour.ImageUrl);
                    cmd.Parameters.AddWithValue("@location", tour.DepartureLocation);
                    cmd.Parameters.AddWithValue("@duration", tour.Duration);
                    cmd.Parameters.AddWithValue("@url", tour.TourDetailUrl);
                    cmd.Parameters.AddWithValue("@dates", string.Join(", ", tour.DepartureDates));
                    cmd.Parameters.AddWithValue("@notes", string.Join("\n\n", tour.ImportantNotes.Select(kv => $"{kv.Key}:\n{kv.Value}")));

                    int tourId = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                    // INSERT lịch trình vào bảng schedules
                    foreach (var item in tour.Schedule)
                    {
                        var scheduleCmd = new MySqlCommand(@"
                            INSERT INTO schedules (tour_id, day_title, day_content)
                            VALUES (@tourId, @title, @content);
                        ", conn);

                        scheduleCmd.Parameters.AddWithValue("@tourId", tourId);
                        scheduleCmd.Parameters.AddWithValue("@title", item.DayTitle);
                        scheduleCmd.Parameters.AddWithValue("@content", item.DayContent);

                        await scheduleCmd.ExecuteNonQueryAsync();
                    }

                    Console.WriteLine($"✅ Insert thành công: {tour.TourName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Lỗi insert DB: {ex.Message}");
                }
            }

            await conn.CloseAsync();
        }
    }
}