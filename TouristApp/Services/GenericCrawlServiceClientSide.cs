using HtmlAgilityPack;
using TouristApp.Models;
using MySqlConnector;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

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

            List<StandardTourModel> tours = new();

            // ✅ Crawl danh sách bằng Selenium (client-side)
            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(config.BaseUrl);

            // Xử lý phân trang động (load_more, carousel)
            if (config.PagingType == "load_more" || config.PagingType == "carousel")
            {
                await HandleDynamicPaging(driver, config.PagingType);
            }

            // Lấy HTML sau khi JS đã render
            var html = driver.PageSource;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);
            Console.WriteLine($"📄 Số lượng tour crawl được: {nodes?.Count ?? 0}");

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
                        };

                        tours.Add(tour);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️ Lỗi parse tour: {ex.Message}");
                    }
                }
            }

            // ✅ Crawl chi tiết bằng Selenium
            foreach (var tour in tours)
            {
                if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                {
                    var fullUrl = tour.TourDetailUrl.StartsWith("http")
                        ? tour.TourDetailUrl
                        : $"{config.BaseDomain.TrimEnd('/')}/{tour.TourDetailUrl.TrimStart('/')}";

                    await CrawlDetailWithSeleniumAsync(tour, fullUrl, config);
                }
            }

            return tours;
        }

        private async Task HandleDynamicPaging(ChromeDriver driver, string pagingType)
        {
            int maxLoad = 10;
            int loadCount = 0;

            while (loadCount < maxLoad)
            {
                try
                {
                    // Scroll xuống cuối trang để load nội dung nếu có lazy load
                    ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                    await Task.Delay(2000); // đợi JS tải xong

                    if (pagingType == "load_more")
                    {
                        // Tìm nút "Xem thêm" hoặc "Load more"
                        var loadMoreButton = driver.FindElements(By.XPath("//button[contains(text(),'Xem thêm') or contains(text(),'Load more') or contains(text(),'Tải thêm')]")).FirstOrDefault();
                        if (loadMoreButton != null && loadMoreButton.Displayed)
                        {
                            loadMoreButton.Click();
                            await Task.Delay(2000);
                        }
                        else
                        {
                            break;
                        }
                    }
                    else if (pagingType == "carousel")
                    {
                        // Xử lý carousel/slider
                        var nextButton = driver.FindElements(By.XPath("//button[contains(@class,'next') or contains(@class,'arrow-right')]")).FirstOrDefault();
                        if (nextButton != null && nextButton.Displayed)
                        {
                            nextButton.Click();
                            await Task.Delay(2000);
                        }
                        else
                        {
                            break;
                        }
                    }

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
            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            try
            {
                using var driver = new ChromeDriver(options);
                driver.Navigate().GoToUrl(url);
                await Task.Delay(2000); // đợi render JS

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

            // Nếu là một đoạn block duy nhất
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
            // Nếu là nhiều block khác nhau
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