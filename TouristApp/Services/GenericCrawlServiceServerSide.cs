using HtmlAgilityPack;
using TouristApp.Models;
using MySqlConnector;

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

            // ✅ Crawl danh sách bằng HtmlAgilityPack (server-side)
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

            // ✅ Crawl chi tiết bằng HtmlAgilityPack
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
            // 📅 Lịch trình
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

            var noteRoots = doc.DocumentNode.SelectNodes(config.TourDetailNote);

            // Nếu là một đoạn block duy nhất (trang LuaViet)
            if (noteRoots == null || noteRoots.Count == 0)
            {
                var noteRoot = doc.DocumentNode.SelectSingleNode(config.TourDetailNote);
                if (noteRoot != null)
                {
                    string currentHeading = "";
                    foreach (var child in noteRoot.ChildNodes)
                    {
                        if (child.NodeType != HtmlNodeType.Element) continue;

                        if (child.Name.StartsWith("h", StringComparison.OrdinalIgnoreCase))
                        {
                            currentHeading = HtmlEntity.DeEntitize(child.InnerText.Trim());
                            if (!tour.ImportantNotes.ContainsKey(currentHeading))
                            {
                                tour.ImportantNotes[currentHeading] = "";
                            }
                        }
                        else if (!string.IsNullOrEmpty(currentHeading))
                        {
                            string content = HtmlEntity.DeEntitize(child.InnerText.Trim());
                            if (!string.IsNullOrWhiteSpace(content))
                            {
                                tour.ImportantNotes[currentHeading] += content + "\n";
                            }
                        }
                    }

                    foreach (var key in tour.ImportantNotes.Keys.ToList())
                        tour.ImportantNotes[key] = tour.ImportantNotes[key].Trim();
                }
            }
            // Nếu là nhiều block khác nhau (ví dụ như trang DeViet sau khi refactor)
            else
            {
                foreach (var noteRoot in noteRoots)
                {
                    var heading = noteRoot.SelectSingleNode(".//h3|.//h4")?.InnerText?.Trim() ?? "";
                    var contentNode = noteRoot.SelectSingleNode(".//ul");
                    var contentText = HtmlEntity.DeEntitize(contentNode?.InnerText?.Trim() ?? "");

                    if (!string.IsNullOrEmpty(heading) && !string.IsNullOrEmpty(contentText))
                    {
                        if (!tour.ImportantNotes.ContainsKey(heading))
                            tour.ImportantNotes[heading] = contentText;
                    }
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