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

            // Phân trang động (load_more, carousel)
            if (config.PagingType == "load_more" || config.PagingType == "carousel")
            {
                await HandleDynamicPagingAsync(driver, config);
            }

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

            // Crawl chi tiết
            foreach (var tour in tours)
            {
                if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                {
                    var fullUrl = tour.TourDetailUrl.StartsWith("http")
                        ? tour.TourDetailUrl
                        : $"{config.BaseDomain?.TrimEnd('/')}/{tour.TourDetailUrl.TrimStart('/')}";

                    await CrawlDetailWithSeleniumAsync(tour, fullUrl, config);
                }
            }

            return tours;
        }

        private async Task HandleDynamicPagingAsync(ChromeDriver driver, PageConfigModel config)
        {
            int maxLoad = 10;
            int loadCount = 0;

            while (loadCount < maxLoad)
            {
                try
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                    await Task.Delay(1200);

                    bool acted = false;

                    // Ưu tiên click theo selector từ DB nếu có
                    if (!string.IsNullOrWhiteSpace(config.LoadMoreButtonSelector))
                    {
                        IReadOnlyCollection<IWebElement> btns;
                        if (string.Equals(config.LoadMoreType, "css", StringComparison.OrdinalIgnoreCase))
                        {
                            btns = driver.FindElements(By.CssSelector(config.LoadMoreButtonSelector));
                        }
                        else // mặc định dùng XPath/class-text fallback
                        {
                            btns = driver.FindElements(By.XPath(config.LoadMoreButtonSelector));
                        }

                        var btn = btns.FirstOrDefault(e => e.Displayed && e.Enabled);
                        if (btn != null)
                        {
                            btn.Click();
                            acted = true;
                            await Task.Delay(1200);
                        }
                    }
                    else
                    {
                        // Fallback theo text như code cũ
                        if (config.PagingType == "load_more")
                        {
                            var loadMoreButton = driver.FindElements(By.XPath("//button[contains(text(),'Xem thêm') or contains(text(),'Load more') or contains(text(),'Tải thêm')]")).FirstOrDefault();
                            if (loadMoreButton != null && loadMoreButton.Displayed)
                            {
                                loadMoreButton.Click();
                                acted = true;
                                await Task.Delay(1200);
                            }
                        }
                        else if (config.PagingType == "carousel")
                        {
                            var nextButton = driver.FindElements(By.XPath("//button[contains(@class,'next') or contains(@class,'arrow-right')]")).FirstOrDefault();
                            if (nextButton != null && nextButton.Displayed)
                            {
                                nextButton.Click();
                                acted = true;
                                await Task.Delay(1200);
                            }
                        }
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
                await Task.Delay(1200);

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
    }
}
