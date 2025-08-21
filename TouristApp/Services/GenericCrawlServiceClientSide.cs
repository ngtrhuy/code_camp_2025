using HtmlAgilityPack;
using TouristApp.Models;
using MySqlConnector;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TouristApp.Services
{
    public class GenericCrawlServiceClientSide : IGenericCrawlService
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
            return await CrawlFromConfigAsync(config);
        }

        // NEW
        public async Task<List<StandardTourModel>> CrawlFromConfigAsync(PageConfigModel config)
        {
            List<StandardTourModel> tours = new();

            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(config.BaseUrl);

            if (config.PagingType == "load_more" || config.PagingType == "carousel")
            {
                await HandleDynamicPagingAsync(driver, config);
            }

            var html = driver.PageSource;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);
            Console.WriteLine($"📄 Số lượng tour crawl được: {nodes?.Count ?? 0}");

            var normalizedBaseDomain = NormalizeBaseDomain(config.BaseDomain); // https://domain
            var hostOnly = GetHost(normalizedBaseDomain);                      // domain

            if (nodes != null)
            {
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
                            SourceSite = hostOnly
                        };

                        if (string.IsNullOrWhiteSpace(tour.TourCode))
                            tour.TourCode = MakeSafeCode(tour.TourName, tour.TourDetailUrl);
                        tour.Duration = TrimDuration(tour.Duration);

                        tours.Add(tour);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Lỗi parse tour: {ex.Message}");
                    }
                }
            }

            foreach (var tour in tours)
            {
                if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                {
                    var fullUrl = BuildAbsoluteUrl(normalizedBaseDomain, tour.TourDetailUrl);
                    await CrawlDetailWithSeleniumAsync(tour, fullUrl, config);
                }
            }

            return tours;
        }

        private async Task HandleDynamicPagingAsync(ChromeDriver driver, PageConfigModel config)
        {
            int maxLoad = 10, loadCount = 0;
            while (loadCount < maxLoad)
            {
                try
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                    await Task.Delay(2000);

                    bool acted = false;

                    // Ưu tiên click theo selector từ DB nếu có
                    if (!string.IsNullOrWhiteSpace(config.LoadMoreButtonSelector))
                    {
                        var btn = driver.FindElements(By.XPath("//button[contains(text(),'Xem thêm') or contains(text(),'Load more') or contains(text(),'Tải thêm')]")).FirstOrDefault();
                        if (btn != null && btn.Displayed) { btn.Click(); await Task.Delay(2000); } else break;
                    }
                    else
                    {
                        var next = driver.FindElements(By.XPath("//button[contains(@class,'next') or contains(@class,'arrow-right')]")).FirstOrDefault();
                        if (next != null && next.Displayed) { next.Click(); await Task.Delay(2000); } else break;
                    }

                    if (!acted) break;
                    loadCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Lỗi xử lý phân trang: {ex.Message}");
                    break;
                }
            }
        }

        private string GetText(HtmlNode node, string xpath) =>
            node.SelectSingleNode(xpath)?.InnerText.Trim() ?? string.Empty;

        private string GetAttribute(HtmlNode node, string xpath, string attr) =>
            node.SelectSingleNode(xpath)?.GetAttributeValue(attr, "") ?? string.Empty;

        private List<string> GetMultipleTexts(HtmlNode node, string xpath) =>
            node.SelectNodes(xpath)?.Select(n => n.InnerText.Trim()).ToList() ?? new List<string>();

        private async Task CrawlDetailWithSeleniumAsync(StandardTourModel tour, string url, PageConfigModel config)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            try
            {
                using var driver = new ChromeDriver(options);
                driver.Navigate().GoToUrl(url);
                await Task.Delay(2000);

                var html = driver.PageSource;
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                ParseTourDetailFromHtml(doc, tour, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi crawl chi tiết (Selenium): {ex.Message}");
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

            var noteRoots = doc.DocumentNode.SelectNodes(config.TourDetailNote);

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
                                tour.ImportantNotes[currentHeading] = "";
                        }
                        else if (!string.IsNullOrEmpty(currentHeading))
                        {
                            string content = HtmlEntity.DeEntitize(child.InnerText.Trim());
                            if (!string.IsNullOrWhiteSpace(content))
                                tour.ImportantNotes[currentHeading] += content + "\n";
                        }
                    }

                    foreach (var key in tour.ImportantNotes.Keys.ToList())
                        tour.ImportantNotes[key] = tour.ImportantNotes[key].Trim();
                }
            }
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
                    LoadMoreButtonSelector = SafeGetString(reader, "load_more_button_selector"),
                    LoadMoreType = SafeGetString(reader, "load_more_type"),
                };
            }

            return null;
        }

        // ===== helpers =====
        private static string NormalizeBaseDomain(string baseDomain)
        {
            var s = (baseDomain ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return "";
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                s = "https://" + s;
            return s.TrimEnd('/');
        }

        private static string GetHost(string normalizedBaseDomain)
        {
            if (Uri.TryCreate(normalizedBaseDomain, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host))
                return u.Host.ToLowerInvariant();
            return normalizedBaseDomain
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Trim('/')
                .ToLowerInvariant();
        }

        private static string BuildAbsoluteUrl(string normalizedBaseDomain, string maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return normalizedBaseDomain;
            if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out var abs)) return abs.ToString();
            return $"{normalizedBaseDomain.TrimEnd('/')}/{maybeRelative.TrimStart('/')}";
        }

        private static string ToAscii(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = HtmlEntity.DeEntitize(s);
            var norm = t.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in norm)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string MakeSafeCode(string? name, string? url)
        {
            string raw = "";
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var u))
                        raw = u.Segments.Last().Trim('/');
                    else
                        raw = url.Split('/').Last();
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(raw)) raw = name ?? "";

            raw = ToAscii(raw).ToUpperInvariant();
            raw = Regex.Replace(raw, @"[^A-Z0-9]+", "");
            if (string.IsNullOrWhiteSpace(raw)) raw = "TOUR" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            return raw.Length > 80 ? raw[..80] : raw;
        }

        private static string TrimDuration(string? s)
        {
            s = (s ?? "").Trim();
            return s.Length > 250 ? s[..250] : s;
        }
    }
}
