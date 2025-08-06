using HtmlAgilityPack;
using MySqlConnector;
using System.Text.RegularExpressions;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class GenericCrawlServiceServerSide : IGenericCrawlService
    {
        private readonly string _connStr = "server=localhost;database=code_camp_2025;uid=root;pwd=;";

        string SafeGetString(MySqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        public async Task<List<StandardTourModel>> CrawlFromPageConfigAsync(int configId)
        {
            var config = await LoadPageConfig(configId);
            if (config == null) return new List<StandardTourModel>();

            List<StandardTourModel> tours = new();

            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

            int currentPage = 1;
            bool hasMore = true;
            string baseUrl = config.BaseUrl.TrimEnd('/');

            while (hasMore)
            {
                string pageUrl = baseUrl;

                if (config.PagingType == "querystring")
                {
                    pageUrl += pageUrl.Contains("?") ? $"&page={currentPage}" : $"?page={currentPage}";
                }

                Console.WriteLine($"🔍 Trang {currentPage} URL: {pageUrl}");

                var html = await client.GetStringAsync(pageUrl);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);
                Console.WriteLine($"📄 Số lượng tour ở page {currentPage}: {nodes?.Count ?? 0}");

                if (nodes == null || nodes.Count == 0)
                {
                    hasMore = false;
                    break;
                }

                foreach (var node in nodes)
                {
                    try
                    {
                        var tour = new StandardTourModel
                        {
                            TourName = GetText(node, config.TourName),
                            TourCode = GetText(node, config.TourCode),
                            Price = GetText(node, config.TourPrice),
                            ImageUrl = GetAttribute(node, config.ImageUrl, config.ImageAttr),
                            DepartureLocation = GetText(node, config.DepartureLocation),
                            DepartureDates = GetMultipleTexts(node, config.DepartureDate),
                            Duration = GetText(node, config.TourDuration),
                            TourDetailUrl = GetAttribute(node, config.TourDetailUrl, config.TourDetailAttr),
                        };

                        tours.Add(tour);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Lỗi parse tour ở page {currentPage}: {ex.Message}");
                    }
                }

                currentPage++;
                if (config.PagingType != "querystring")
                    break;
            }

            foreach (var tour in tours)
            {
                if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                {
                    var fullUrl = tour.TourDetailUrl.StartsWith("http")
                        ? tour.TourDetailUrl
                        : $"{config.BaseDomain.TrimEnd('/')}/{tour.TourDetailUrl.TrimStart('/')}";

                    await CrawlDetailWithHtmlAgilityPackAsync(tour, fullUrl, config);
                }
            }

            return tours;
        }

        private string GetText(HtmlNode node, string xpath) =>
            node.SelectSingleNode(xpath)?.InnerText.Trim() ?? string.Empty;

        private string GetAttribute(HtmlNode node, string xpath, string attr) =>
            node.SelectSingleNode(xpath)?.GetAttributeValue(attr, "") ?? string.Empty;

        private List<string> GetMultipleTexts(HtmlNode node, string xpath) =>
            node.SelectNodes(xpath)?.Select(n => n.InnerText.Trim()).ToList() ?? new List<string>();

        private async Task CrawlDetailWithHtmlAgilityPackAsync(StandardTourModel tour, string url, PageConfigModel config)
        {
            try
            {
                var doc = await new HtmlWeb().LoadFromWebAsync(url);
                ParseTourDetailFromHtml(doc, tour, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi crawl chi tiết (HTML): {ex.Message}");
            }
        }

        private void ParseTourDetailFromHtml(HtmlDocument doc, StandardTourModel tour, PageConfigModel config)
        {
            var days = doc.DocumentNode.SelectNodes(config.TourDetailDayTitle);
            var contents = doc.DocumentNode.SelectNodes(config.TourDetailDayContent);

            if (days != null && contents != null && days.Count == contents.Count)
            {
                for (int i = 0; i < days.Count; i++)
                {
                    tour.Schedule.Add(new TourScheduleItem
                    {
                        DayTitle = HtmlEntity.DeEntitize(days[i].InnerText.Trim()),
                        DayContent = HtmlEntity.DeEntitize(contents[i].InnerText.Trim())
                    });
                }
            }

            var noteRoot = doc.DocumentNode.SelectSingleNode(config.TourDetailNote);
            tour.ImportantNotes = new Dictionary<string, string>();

            if (noteRoot != null)
            {
                string currentHeading = null;
                List<string> buffer = new List<string>();

                foreach (var child in noteRoot.Descendants())
                {
                    if (child.NodeType != HtmlNodeType.Element) continue;

                    if (child.Name == "strong")
                    {
                        // Lưu heading cũ nếu có
                        if (currentHeading != null && buffer.Count > 0)
                        {
                            tour.ImportantNotes[currentHeading] = string.Join("\n", buffer).Trim();
                            buffer.Clear();
                        }

                        currentHeading = HtmlEntity.DeEntitize(child.InnerText.Trim().TrimEnd(':', '.', ' '));
                    }
                    else if (!string.IsNullOrEmpty(currentHeading))
                    {
                        var text = HtmlEntity.DeEntitize(child.InnerText.Trim());
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            buffer.Add(text);
                        }
                    }
                }

                // Lưu phần cuối
                if (currentHeading != null && buffer.Count > 0)
                {
                    tour.ImportantNotes[currentHeading] = string.Join("\n", buffer).Trim();
                }
            }








            // ✅ Trích xuất ngày khởi hành từ ul.tdetail-date nếu chưa có
            if (tour.DepartureDates == null || tour.DepartureDates.Count == 0)
            {
                var dateNodes = doc.DocumentNode.SelectNodes("//ul[contains(@class, 'tdetail-date')]/li");
                if (dateNodes != null)
                {
                    var extractedDates = dateNodes
                        .Select(li => li.InnerText.Trim())
                        .Where(date => !string.IsNullOrWhiteSpace(date))
                        .Select(date => Regex.Match(date, @"\d{1,2}/\d{1,2}").Value)
                        .Where(date => !string.IsNullOrWhiteSpace(date))
                        .Distinct()
                        .ToList();

                    tour.DepartureDates = extractedDates;
                    Console.WriteLine($"📅 Lấy {extractedDates.Count} ngày khởi hành từ ul.tdetail-date cho tour: {tour.TourName}");
                }
            }
        }

        public async Task<PageConfigModel?> LoadPageConfig(int id)
        {
            using var conn = new MySqlConnection(_connStr);
            await conn.OpenAsync();

            var cmd = new MySqlCommand("SELECT * FROM page_config WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PageConfigModel
                {
                    BaseDomain = SafeGetString(reader, "base_domain"),
                    BaseUrl = SafeGetString(reader, "base_url"),
                    TourName = SafeGetString(reader, "tour_name"),
                    TourCode = SafeGetString(reader, "tour_code"),
                    TourPrice = SafeGetString(reader, "tour_price"),
                    ImageUrl = SafeGetString(reader, "image_url"),
                    DepartureLocation = SafeGetString(reader, "departure_location"),
                    DepartureDate = SafeGetString(reader, "departure_date"),
                    TourDuration = SafeGetString(reader, "tour_duration"),
                    TourDetailUrl = SafeGetString(reader, "tour_detail_url"),
                    TourDetailDayTitle = SafeGetString(reader, "tour_detail_day_title"),
                    TourDetailDayContent = SafeGetString(reader, "tour_detail_day_content"),
                    TourDetailNote = SafeGetString(reader, "tour_detail_note"),
                    CrawlType = SafeGetString(reader, "crawl_type"),
                    TourListSelector = SafeGetString(reader, "tour_list_selector"),
                    ImageAttr = SafeGetString(reader, "image_attr"),
                    TourDetailAttr = SafeGetString(reader, "tour_detail_attr"),
                    PagingType = SafeGetString(reader, "paging_type"),
                };
            }

            return null;
        }
    }
}
