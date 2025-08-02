using HtmlAgilityPack;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class LuaVietTourCrawler
    {
        private readonly string _baseUrl = "https://www.luavietours.com/du-lich";
        private static readonly int[] TourCategoryIds = { 2, 3, 263 }; // Ngoài nước, trong nước, cao cấp

        public static string CleanText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var cleaned = input
                .Replace("\r", "")
                .Replace("\t", "")
                .Replace("\u00A0", " ")
                .Replace("&nbsp;", " ")
                .Replace("\n", "\n"); // giữ \n để sau này xử lý thành đoạn nếu cần

            var lines = cleaned.Split('\n')
                               .Select(line => line.Trim())
                               .Where(line => !string.IsNullOrWhiteSpace(line));
            return string.Join("\n", lines).Trim();
        }
        public async Task<List<StandardTourModel>> CrawlAndInsertToDatabaseAsync()
        {
            var tours = await CrawlAllCategoriesAsync();
            await InsertToursToDatabase(tours);
            return tours;
        }

        public async Task<List<StandardTourModel>> CrawlAllCategoriesAsync()
        {
            var result = new List<StandardTourModel>();

            foreach (var tourId in TourCategoryIds)
            {
                int currentPage = 1;
                while (true)
                {
                    string url = $"{_baseUrl}?page={currentPage}&tourid={tourId}&dong_tour=&diem_di=&diem_den=&so_ngay=&so_nguoi=&min_price=&max_price=200&khuyen_mai=&con_cho=&sort=undefined";
                    Console.WriteLine($"🔄 Crawl tourid={tourId}, page={currentPage}");

                    using var client = new HttpClient();
                    var html = await client.GetStringAsync(url);

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var tourNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'item hoverZoom publish')]");
                    if (tourNodes == null || tourNodes.Count == 0) break;

                    foreach (var node in tourNodes)
                    {
                        try
                        {
                            var tour = new StandardTourModel
                            {
                                TourName = node.SelectSingleNode(".//div[contains(@class,'item__ttl')]")?.InnerText.Trim(),
                                TourCode = node.SelectSingleNode(".//div[contains(@class,'c-code')]/p[@class='txt']")?.InnerText.Trim(),
                                Price = node.SelectSingleNode(".//div[contains(@class,'c-price')]")?.InnerText.Trim(),
                                ImageUrl = node.SelectSingleNode(".//div[contains(@class,'item__img')]/img")?.GetAttributeValue("src", null),
                                DepartureLocation = node.SelectSingleNode(".//div[contains(@class,'start')]/p[@class='txt']")?.InnerText.Trim(),
                                DepartureDates = node.SelectNodes(".//div[contains(@class,'item__ngaydi')]/a")?.Select(a => a.InnerText.Trim()).ToList() ?? new List<string>(),
                                Duration = node.SelectSingleNode(".//div[contains(@class,'item__datetime')]/span")?.InnerText.Replace("Thời lượng:", "").Trim(),
                                TourDetailUrl = node.SelectSingleNode(".//a[@class='main-link']")?.GetAttributeValue("href", null)
                            };

                            // Crawl chi tiết tour
                            if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                            {
                                tour.Schedule = await CrawlTourDetailAsync(tour.TourDetailUrl, tour);
                            }

                            result.Add(tour);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Lỗi tour (page {currentPage}): {ex.Message}");
                        }
                    }

                    currentPage++;
                }
            }

            return result;
        }

        private async Task<List<TourScheduleItem>> CrawlTourDetailAsync(string tourDetailUrl, StandardTourModel tour)
        {
            var scheduleResult = new List<TourScheduleItem>();
            if (string.IsNullOrWhiteSpace(tourDetailUrl)) return scheduleResult;

            try
            {
                var doc = await new HtmlWeb().LoadFromWebAsync(tourDetailUrl);

                // -------- Crawl lịch trình --------
                var programNode = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'etr--program')]");
                if (programNode != null)
                {
                    var anchorNodes = programNode.SelectNodes(".//div[contains(@class,'anchor')]");
                    if (anchorNodes != null)
                    {
                        foreach (var anchor in anchorNodes)
                        {
                            var headingNode = anchor.SelectSingleNode(".//div[contains(@class,'anchor__heading')]");
                            var dateNode = headingNode?.SelectSingleNode(".//p[contains(@class,'date')]")?.InnerText.Trim();
                            var ttlNode = headingNode?.SelectSingleNode(".//p[contains(@class,'ttl')]")?.InnerText.Trim();
                            var dayTitle = $"{dateNode} {ttlNode}".Trim();

                            var txtNode = anchor.SelectSingleNode(".//div[contains(@class,'anchor__cont')]/div[contains(@class,'txt')]");
                            string combinedContent = "";

                            if (txtNode != null)
                            {
                                var paragraphs = txtNode.SelectNodes("./p");
                                if (paragraphs != null)
                                {
                                    foreach (var p in paragraphs)
                                    {
                                        var plainText = HtmlEntity.DeEntitize(p.InnerText.Trim());
                                        if (!string.IsNullOrWhiteSpace(plainText))
                                        {
                                            combinedContent += plainText + "\n";
                                        }
                                    }
                                }
                                else
                                {
                                    combinedContent = HtmlEntity.DeEntitize(txtNode.InnerText.Trim());
                                }
                            }

                            if (!string.IsNullOrWhiteSpace(dayTitle) || !string.IsNullOrWhiteSpace(combinedContent))
                            {
                                scheduleResult.Add(new TourScheduleItem
                                {
                                    DayTitle = dayTitle,
                                    DayContent = combinedContent.Trim()
                                });
                            }
                        }
                    }
                }
                // -------- Crawl chú ý --------
                var noteSection = doc.DocumentNode.SelectSingleNode("//div[@class='c-hide']//div[contains(@class,'editor') and contains(@class,'cms-content')]");
                if (noteSection != null)
                {
                    string currentHeading = "";
                    var children = noteSection.ChildNodes;

                    foreach (var child in children)
                    {
                        if (child.Name == "h4")
                        {
                            currentHeading = HtmlEntity.DeEntitize(child.InnerText.Trim());
                            if (!tour.ImportantNotes.ContainsKey(currentHeading))
                            {
                                tour.ImportantNotes[currentHeading] = "";
                            }
                        }
                        else if (!string.IsNullOrEmpty(currentHeading))
                        {
                            var textContent = HtmlEntity.DeEntitize(child.InnerText.Trim());
                            if (!string.IsNullOrWhiteSpace(textContent))
                            {
                                tour.ImportantNotes[currentHeading] += textContent + "\n";
                            }
                        }
                    }

                    foreach (var key in tour.ImportantNotes.Keys.ToList())
                    {
                        tour.ImportantNotes[key] = tour.ImportantNotes[key].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi crawl chi tiết: {ex.Message}");
            }
            return scheduleResult;
        }
        private async Task InsertToursToDatabase(List<StandardTourModel> tours)
        {
            string connStr = "server=localhost;database=code_camp_2025;uid=root;pwd=;";

            using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();

            foreach (var tour in tours)
            {
                try
                {
                    // ✅ Kiểm tra trùng lặp tour dựa vào tour_name, price và duration
                    var checkCmd = new MySqlCommand(@"
                SELECT COUNT(*) FROM tours 
                WHERE tour_name = @name AND price = @price AND duration = @duration
            ", conn);

                    checkCmd.Parameters.AddWithValue("@name", tour.TourName);
                    checkCmd.Parameters.AddWithValue("@price", tour.Price);
                    checkCmd.Parameters.AddWithValue("@duration", tour.Duration);

                    int existingCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
                    if (existingCount > 0)
                    {
                        Console.WriteLine($"⏩ Bỏ qua tour trùng: {tour.TourName}");
                        continue;
                    }

                    // ✅ INSERT vào bảng tours
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

                    // ✅ INSERT lịch trình vào bảng schedules
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
