using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MySqlConnector;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class PystravelCrawlService
    {
        private const string ListUrl = "https://pystravel.vn/danh-muc-tour/508-tour-mua-thu-trong-nuoc.html";
        private readonly string _connStr = "server=localhost;database=code_camp_2025;uid=root;pwd=;";

        public async Task<List<StandardTourModel>> CrawlListAsync(bool includeDetails = true)
        {
            var results = new List<StandardTourModel>();
            using var http = CreateHttp();

            var html = await http.GetStringAsync(ListUrl);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Mỗi thẻ card
            var itemNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'grid') and contains(@class,'shadow') and .//h2]");
            if (itemNodes == null || itemNodes.Count == 0) return results;

            foreach (var node in itemNodes)
            {
                try
                {
                    // Ảnh
                    var img = node.SelectSingleNode(".//img");
                    var imgSrc = img?.GetAttributeValue("src", "")?.Trim() ?? "";
                    var imageUrl = string.IsNullOrWhiteSpace(imgSrc)
                        ? ""
                        : (imgSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? imgSrc : $"https://pystravel.vn{imgSrc}");

                    // Tiêu đề
                    var title = HtmlEntity.DeEntitize(node.SelectSingleNode(".//h2")?.InnerText?.Trim() ?? "");

                    // Giá
                    var priceNode = node.SelectSingleNode(".//div[contains(@class,'text-xl') and contains(@class,'font-bold') and contains(@class,'text-tertiary')]");
                    var price = HtmlEntity.DeEntitize(priceNode?.InnerText?.Trim() ?? "");

                    // 3 dòng info
                    var liNodes = node.SelectNodes(".//div[contains(@class,'flex') and contains(@class,'gap-2')]//ul/li");
                    string departureLocation = "", duration = "";
                    var departureDates = new List<string>();

                    if (liNodes != null && liNodes.Count >= 3)
                    {
                        departureLocation = CleanColonText(liNodes[0].InnerText);  // Điểm khởi hành
                        duration = CleanColonText(liNodes[1].InnerText);           // Thời gian
                        var rawDepart = HtmlEntity.DeEntitize(liNodes[2].InnerText ?? "");
                        departureDates = ParseDepartureDates(rawDepart);           // Khởi hành: Tháng xx: ...
                    }

                    // Link chi tiết
                    var detailLink = node.SelectSingleNode(".//a[contains(@class,'main-link')]")
                                   ?? node.SelectSingleNode(".//h2/ancestor-or-self::a[1]")
                                   ?? node.SelectSingleNode(".//button/ancestor::a[1]");
                    var detailUrl = detailLink?.GetAttributeValue("href", "")?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(detailUrl) && !detailUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                        detailUrl = $"https://pystravel.vn{detailUrl}";

                    // Ưu đãi
                    var promoLis = node.SelectNodes(".//ul[contains(@class,'list-disc')]/li");
                    var promos = promoLis?.Select(li => HtmlEntity.DeEntitize(li.InnerText.Trim()))
                                         .Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                                 ?? new List<string>();

                    var tour = new StandardTourModel
                    {
                        TourName = title,
                        TourCode = "",
                        Price = price,
                        ImageUrl = imageUrl,
                        DepartureLocation = departureLocation,
                        Duration = duration,
                        TourDetailUrl = detailUrl,
                        DepartureDates = departureDates
                    };

                    if (promos.Count > 0)
                        tour.ImportantNotes["Ưu đãi"] = string.Join("\n", promos);

                    // ⭐ Crawl trang chi tiết để lấy lịch trình
                    if (includeDetails && !string.IsNullOrWhiteSpace(detailUrl))
                    {
                        await CrawlTourDetailAsync(detailUrl, tour);
                    }

                    results.Add(tour);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Pystravel] Lỗi parse card: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Crawl chi tiết: các block "NGÀY 01/02/..." + nội dung
        /// </summary>
        public async Task CrawlTourDetailAsync(string detailUrl, StandardTourModel tour)
        {
            try
            {
                using var http = CreateHttp();
                var html = await http.GetStringAsync(detailUrl);
                var doc = new HtmlDocument(); doc.LoadHtml(html);

                // Mỗi block ngày
                var dayBlocks = doc.DocumentNode.SelectNodes("//div[contains(@class,'border-dashed') and contains(@class,'border-l')]");
                if (dayBlocks == null || dayBlocks.Count == 0) return;

                int idx = 1;
                foreach (var block in dayBlocks)
                {
                    // H3
                    var dayTitleRaw = block.SelectSingleNode(".//h3")?.InnerText ?? "";
                    var dayTitle = NormalizeSpaces(HtmlEntity.DeEntitize(dayTitleRaw));

                    // Nội dung: vùng chứa description
                    var contentNode = block.SelectSingleNode(".//div[contains(@class,'tour-detail_tour-content')]")
                                       ?? block.SelectSingleNode(".//div[contains(@class,'collapsible_inner')]");
                    var dayContent = contentNode == null ? "" : HtmlToMultilineText(contentNode);

                    if (!string.IsNullOrWhiteSpace(dayTitle) || !string.IsNullOrWhiteSpace(dayContent))
                    {
                        tour.Schedule.Add(new TourScheduleItem
                        {
                            Id = idx++,
                            DayTitle = dayTitle,
                            DayContent = dayContent
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Pystravel] Lỗi crawl detail {detailUrl}: {ex.Message}");
            }
        }

        /// <summary>Ghi DB: tours + schedules. Bỏ qua tour trùng (tour_name, price, duration)</summary>
        public async Task<int> ImportAsync(bool includeDetails = true)
        {
            var tours = await CrawlListAsync(includeDetails);

            using var conn = new MySqlConnection(_connStr);
            await conn.OpenAsync();

            int inserted = 0;
            foreach (var t in tours)
            {
                try
                {
                    // Check trùng
                    var checkCmd = new MySqlCommand(@"
                        SELECT id FROM tours
                        WHERE tour_name=@name AND price=@price AND duration=@dur
                        LIMIT 1", conn);
                    checkCmd.Parameters.AddWithValue("@name", t.TourName);
                    checkCmd.Parameters.AddWithValue("@price", t.Price);
                    checkCmd.Parameters.AddWithValue("@dur", t.Duration);

                    var existIdObj = await checkCmd.ExecuteScalarAsync();
                    if (existIdObj != null && existIdObj != DBNull.Value)
                    {
                        Console.WriteLine($"⏩ Bỏ qua tour trùng: {t.TourName}");
                        continue;
                    }

                    // Insert tour
                    var ins = new MySqlCommand(@"
                        INSERT INTO tours(
                            tour_name, tour_code, price, image_url,
                            departure_location, duration, tour_detail_url,
                            departure_dates, important_notes
                        ) VALUES(
                            @name,@code,@price,@img,
                            @loc,@dur,@url,
                            @dates,@notes
                        );
                        SELECT LAST_INSERT_ID();", conn);

                    ins.Parameters.AddWithValue("@name", t.TourName);
                    ins.Parameters.AddWithValue("@code", t.TourCode);
                    ins.Parameters.AddWithValue("@price", t.Price);
                    ins.Parameters.AddWithValue("@img", t.ImageUrl);
                    ins.Parameters.AddWithValue("@loc", t.DepartureLocation);
                    ins.Parameters.AddWithValue("@dur", t.Duration);
                    ins.Parameters.AddWithValue("@url", t.TourDetailUrl);
                    ins.Parameters.AddWithValue("@dates", string.Join(", ", t.DepartureDates ?? new List<string>()));
                    ins.Parameters.AddWithValue("@notes", string.Join("\n\n", t.ImportantNotes.Select(kv => $"{kv.Key}:\n{kv.Value}")));

                    int tourId = Convert.ToInt32(await ins.ExecuteScalarAsync());
                    inserted++;

                    // Insert schedules (nếu có)
                    if (t.Schedule != null && t.Schedule.Count > 0)
                    {
                        foreach (var s in t.Schedule)
                        {
                            var insSch = new MySqlCommand(@"
                                INSERT INTO schedules(tour_id, day_title, day_content)
                                VALUES(@tourId, @title, @content)", conn);
                            insSch.Parameters.AddWithValue("@tourId", tourId);
                            insSch.Parameters.AddWithValue("@title", s.DayTitle ?? "");
                            insSch.Parameters.AddWithValue("@content", s.DayContent ?? "");
                            await insSch.ExecuteNonQueryAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Lỗi insert: {ex.Message}");
                }
            }

            await conn.CloseAsync();
            return inserted;
        }

        // ================= Helpers =================

        private static HttpClient CreateHttp()
        {
            var h = new HttpClient();
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TourCrawler/1.0)");
            return h;
        }

        // "Khởi hành: Tháng 08: 14; Tháng 09: 11, 18, 25; ..."
        private static List<string> ParseDepartureDates(string raw)
        {
            var res = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return res;

            var matches = Regex.Matches(raw, @"Tháng\s*(\d{1,2})\s*:\s*([^;]+)");
            foreach (Match m in matches)
            {
                var mm = m.Groups[1].Value.PadLeft(2, '0');
                var days = Regex.Matches(m.Groups[2].Value, @"\d{1,2}")
                                .Cast<Match>()
                                .Select(x => x.Value.PadLeft(2, '0'));
                res.AddRange(days.Select(d => $"{mm}-{d}"));
            }
            return res.Distinct().ToList();
        }

        private static string CleanColonText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            var s = HtmlEntity.DeEntitize(input).Trim();
            var i = s.IndexOf(':');
            return (i >= 0 && i + 1 < s.Length) ? s[(i + 1)..].Trim() : s;
        }

        private static string NormalizeSpaces(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("&nbsp;", " ");
            s = HtmlEntity.DeEntitize(s);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        /// <summary>Chuyển HTML -> text, giữ xuống dòng cho p/br/li.</summary>
        private static string HtmlToMultilineText(HtmlNode node)
        {
            // Clone để thao tác
            var temp = HtmlNode.CreateNode(node.OuterHtml);

            foreach (var br in temp.SelectNodes(".//br") ?? Enumerable.Empty<HtmlNode>())
                br.ParentNode.ReplaceChild(HtmlTextNode.CreateNode("\n"), br);

            foreach (var p in temp.SelectNodes(".//p") ?? Enumerable.Empty<HtmlNode>())
                p.InnerHtml = (p.InnerHtml?.Trim() ?? "") + "\n";

            foreach (var li in temp.SelectNodes(".//li") ?? Enumerable.Empty<HtmlNode>())
            {
                var text = HtmlEntity.DeEntitize(li.InnerText.Trim());
                li.ParentNode.ReplaceChild(HtmlTextNode.CreateNode("- " + text + "\n"), li);
            }

            foreach (var bad in temp.SelectNodes(".//img|.//script|.//style") ?? Enumerable.Empty<HtmlNode>())
                bad.Remove();

            var txt = HtmlEntity.DeEntitize(temp.InnerText ?? "");
            txt = Regex.Replace(txt, @"[ \t]+\n", "\n");
            txt = Regex.Replace(txt, @"\n{3,}", "\n\n");
            return txt.Trim();
        }
    }
}
