using HtmlAgilityPack;
using MySqlConnector;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Net;
using System.Text.RegularExpressions;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class GenericCrawlServiceClientSide : IGenericCrawlService
    {
        private readonly TourRepository _tourRepository;

        public GenericCrawlServiceClientSide()
        {
            var config = new ConfigurationBuilder().AddJsonFile("appsettings.json").Build();
            _tourRepository = new TourRepository(config);
        }

        public async Task<List<StandardTourModel>> CrawlFromPageConfigAsync(int configId)
            => await CrawlFromPageConfigAsync(configId, 100);

        public async Task<List<StandardTourModel>> CrawlFromPageConfigAsync(int configId, int maxTours)
        {
            var config = await LoadPageConfig(configId);
            if (config == null) return new List<StandardTourModel>();

            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            List<StandardTourModel> tours = new();

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(config.BaseUrl);

            // Paging kiểu "load more"
            /*  if (config.PagingType == "load_more")
              {
                  while (true)
                  {
                      var currentDoc = new HtmlDocument();
                      currentDoc.LoadHtml(driver.PageSource);
                      var currentNodes = currentDoc.DocumentNode.SelectNodes(config.TourListSelector);
                      int currentCount = currentNodes?.Count ?? 0;

                      if (currentCount >= maxTours) break;

                      IWebElement? loadMoreButton = null;
                      if (!string.IsNullOrEmpty(config.LoadMoreButtonSelector))
                      {
                          if (config.LoadMoreType == "id")
                              loadMoreButton = driver.FindElements(By.Id(config.LoadMoreButtonSelector.Replace("#", ""))).FirstOrDefault();
                          else if (config.LoadMoreType == "class")
                              loadMoreButton = driver.FindElements(By.ClassName(config.LoadMoreButtonSelector.Replace(".", ""))).FirstOrDefault();
                          else if (config.LoadMoreType == "xpath")
                              loadMoreButton = driver.FindElements(By.XPath(config.LoadMoreButtonSelector)).FirstOrDefault();
                      }

                      if (loadMoreButton == null) break;

                      var js = (IJavaScriptExecutor)driver;
                      js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", loadMoreButton);
                      js.ExecuteScript("arguments[0].click();", loadMoreButton);
                      Thread.Sleep(6000);

                      var newDoc = new HtmlDocument();
                      newDoc.LoadHtml(driver.PageSource);
                      var newCount = newDoc.DocumentNode.SelectNodes(config.TourListSelector)?.Count ?? 0;
                      if (newCount <= currentCount) break;
                  }
              }*/
            if (config.PagingType == "load_more")
            {
                int prevCount = 0;
                while (true)
                {
                    var currentDoc = new HtmlDocument();
                    currentDoc.LoadHtml(driver.PageSource);
                    var currentNodes = currentDoc.DocumentNode.SelectNodes(config.TourListSelector);
                    int currentCount = currentNodes?.Count ?? 0;

                    IWebElement? loadMoreButton = null;
                    if (!string.IsNullOrEmpty(config.LoadMoreButtonSelector))
                    {
                        if (config.LoadMoreType == "id")
                            loadMoreButton = driver.FindElements(By.Id(config.LoadMoreButtonSelector.Replace("#", ""))).FirstOrDefault();
                        else if (config.LoadMoreType == "class")
                            loadMoreButton = driver.FindElements(By.ClassName(config.LoadMoreButtonSelector.Replace(".", ""))).FirstOrDefault();
                        else if (config.LoadMoreType == "xpath")
                            loadMoreButton = driver.FindElements(By.XPath(config.LoadMoreButtonSelector)).FirstOrDefault();
                    }

                    // Nếu không còn nút loadmore hoặc không còn tour mới xuất hiện thì break
                    if (loadMoreButton == null || currentCount == prevCount)
                        break;

                    var js = (IJavaScriptExecutor)driver;
                    js.ExecuteScript("arguments[0].scrollIntoView({block:'center'});", loadMoreButton);
                    js.ExecuteScript("arguments[0].click();", loadMoreButton);
                    Thread.Sleep(6000);

                    prevCount = currentCount;
                }
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(driver.PageSource);
            var nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);

            var codeSet = new HashSet<string>();
            var urlSet = new HashSet<string>();

            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    try
                    {
                        var tour = new StandardTourModel
                        {
                            TourName = CleanText(GetTextByXPath(node, config.TourName)),
                            TourCode = config.TourCode == "NULL" ? "" : CleanText(GetTextOrAttr(node, config.TourCode)),
                            Price = CleanText(GetTextByXPath(node, config.TourPrice)),
                            ImageUrl = ToAbsoluteUrl(config.BaseDomain, GetAttrByXPath(node, config.ImageUrl, config.ImageAttr)),

                            TourDetailUrl = GetAttrByXPath(node, config.TourDetailUrl, config.TourDetailAttr),
                            DepartureLocation = CleanText(GetTextByXPath(node, config.DepartureLocation)),
                            Duration = CleanText(GetTextByXPath(node, config.TourDuration)),
                            SourceSite = new Uri(config.BaseDomain).Host.Replace("www.", "")

                        };

                        if (string.IsNullOrWhiteSpace(tour.TourName)
                            && string.IsNullOrWhiteSpace(tour.TourCode)
                            && string.IsNullOrWhiteSpace(tour.TourDetailUrl))
                            continue;

                        if (!string.IsNullOrEmpty(tour.TourDetailUrl) && !tour.TourDetailUrl.StartsWith("http"))
                            tour.TourDetailUrl = config.BaseDomain.TrimEnd('/') + "/" + tour.TourDetailUrl.TrimStart('/');

                        var codeKey = tour.TourCode?.Trim() ?? "";
                        var urlKey = tour.TourDetailUrl?.Trim() ?? "";

                        if (_tourRepository.IsTourExists(codeKey, urlKey))
                            continue;
                        if (!string.IsNullOrWhiteSpace(codeKey) && codeSet.Contains(codeKey)) continue;
                        if (!string.IsNullOrWhiteSpace(urlKey) && urlSet.Contains(urlKey)) continue;
                        if (!string.IsNullOrWhiteSpace(codeKey)) codeSet.Add(codeKey);
                        if (!string.IsNullOrWhiteSpace(urlKey)) urlSet.Add(urlKey);

                        // Crawl detail nếu có url
                        if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                        {
                            try
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("window.open();");
                                var tabs = driver.WindowHandles;
                                driver.SwitchTo().Window(tabs.Last());
                                driver.Navigate().GoToUrl(tour.TourDetailUrl);
                                Thread.Sleep(2000);

                                var detailSource = driver.PageSource;
                                var detailHtml = new HtmlDocument();
                                detailHtml.LoadHtml(detailSource);

                                // Lấy Title ngày: Ưu tiên lấy bằng config, nếu ko có thì fallback 1 số class phổ biến
                                var dayTitles = TrySelectNodesWithFallback(
                                    detailHtml, config.TourDetailDayTitle,
                                    new List<string> {
                                        "//div[contains(@class,'itinerary-box')]//div[contains(@class,'iti-day-title')]",
                                        "//div[contains(@class,'ngaylichtrinh')]",
                                        "//div[contains(@class,'day-title')]"
                                    }
                                );

                                // Lấy Content ngày: giống như trên
                                var dayContents = TrySelectNodesWithFallback(
                                    detailHtml, config.TourDetailDayContent,
                                    new List<string> {
                                        "//div[contains(@class,'itinerary-box')]//div[contains(@class,'iti-day-content')]",
                                        "//div[contains(@class,'noidunglichtrinh')]",
                                        "//div[contains(@class,'day-content')]"
                                    }
                                );

                                tour.Schedule = new List<TourScheduleItem>();
                                for (int i = 0; i < Math.Min(dayTitles.Count, dayContents.Count); i++)
                                {
                                    tour.Schedule.Add(new TourScheduleItem
                                    {
                                        Id = i + 1,
                                        DayTitle = CleanText(dayTitles[i].InnerText),
                                        DayContent = CleanText(dayContents[i].InnerText)
                                    });
                                }

                                // Notes
                                var noteNodes = TrySelectNodesWithFallback(
                                    detailHtml, config.TourDetailNote,
                                    new List<string> {
                                        "//div[contains(@class,'service-policy')]",
                                        "//div[contains(@class,'note')]"
                                    }
                                );

                                tour.ImportantNotes = new Dictionary<string, string>();
                                if (noteNodes.Count > 0)
                                {
                                    var notes = string.Join("\n\n", noteNodes.Select(n => CleanText(n.InnerText)));
                                    tour.ImportantNotes["Ghi chú"] = notes;
                                }

                                // Departure Dates
                                var dateNodes = TrySelectNodesWithFallback(
                                    detailHtml, config.DepartureDate,
                                    new List<string> {
                                        "//div[contains(@class,'list-depart-date')]//span",
                                        "//span[contains(@class,'depart-date')]"
                                    }
                                );
                                if (dateNodes.Count > 0)
                                {
                                    tour.DepartureDates = dateNodes
                                        .Select(x => HtmlEntity.DeEntitize(x.InnerText).Trim())
                                        .Where(s => !string.IsNullOrEmpty(s))
                                        .ToList();
                                }

                                // Code fallback
                                if (string.IsNullOrWhiteSpace(tour.TourCode) && config.TourCode != "NULL")
                                {
                                    tour.TourCode = CleanText(detailHtml.DocumentNode.SelectSingleNode(config.TourCode)?.InnerText ?? "");
                                }

                                var detailDep = CleanText(detailHtml.DocumentNode.SelectSingleNode(config.DepartureLocation)?.InnerText ?? "");
                                if (!string.IsNullOrEmpty(detailDep))
                                    tour.DepartureLocation = detailDep;

                                var detailDur = CleanText(detailHtml.DocumentNode.SelectSingleNode(config.TourDuration)?.InnerText ?? "");
                                if (!string.IsNullOrEmpty(detailDur))
                                    tour.Duration = detailDur;

                                driver.Close();
                                driver.SwitchTo().Window(tabs.First());
                            }
                            catch
                            {
                                try
                                {
                                    var tabs = driver.WindowHandles;
                                    if (tabs.Count > 1)
                                    {
                                        driver.Close();
                                        driver.SwitchTo().Window(tabs.First());
                                    }
                                }
                                catch { }
                            }
                        }

                        if (tour.Schedule == null) tour.Schedule = new List<TourScheduleItem>();
                        if (tour.DepartureDates == null) tour.DepartureDates = new List<string>();
                        if (tour.ImportantNotes == null) tour.ImportantNotes = new Dictionary<string, string>();

                        tours.Add(tour);

                        if (tours.Count >= maxTours) break;
                    }
                    catch { }
                }
            }

            /*            return tours;*/
            return tours.Take(maxTours).ToList();

        }

        // Fallback đa class phổ biến, ưu tiên config
        private static List<HtmlNode> TrySelectNodesWithFallback(HtmlDocument doc, string? primarySelector, List<string> fallbackXpaths)
        {
            List<HtmlNode> nodes = new();
            if (!string.IsNullOrWhiteSpace(primarySelector))
            {
                var n = doc.DocumentNode.SelectNodes(primarySelector);
                if (n != null && n.Count > 0) return n.ToList();
            }
            foreach (var xpath in fallbackXpaths)
            {
                var n = doc.DocumentNode.SelectNodes(xpath);
                if (n != null && n.Count > 0) return n.ToList();
            }
            return nodes;
        }

        private static string GetTextByXPath(HtmlNode context, string xPath)
        {
            if (string.IsNullOrWhiteSpace(xPath)) return string.Empty;
            try
            {
                var node = context.SelectSingleNode(xPath);
                return node != null ? WebUtility.HtmlDecode(node.InnerText ?? string.Empty) : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string GetTextOrAttr(HtmlNode context, string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return string.Empty;
            expr = expr.Trim();
            try
            {
                if (expr.StartsWith("@"))
                {
                    var attrName = expr.TrimStart('@');
                    return context.GetAttributeValue(attrName, string.Empty) ?? string.Empty;
                }
                var node = context.SelectSingleNode(expr);
                return node != null ? WebUtility.HtmlDecode(node.InnerText ?? string.Empty) : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string GetAttrByXPath(HtmlNode context, string xPath, string attrName)
        {
            if (string.IsNullOrWhiteSpace(xPath)) return string.Empty;
            try
            {
                HtmlNode? node = null;
                if (context.Name.ToLower() == "img")
                {
                    node = context;
                }
                else
                {
                    node = context.SelectSingleNode(xPath);
                }

                if (node == null)
                    return string.Empty;

                var name = string.IsNullOrWhiteSpace(attrName) ? "href" : attrName.Trim().ToLower();

                string[] fallbackAttrs;
                if (name == "src")
                    fallbackAttrs = new string[] { "src", "data-src" };
                else if (name == "data-src")
                    fallbackAttrs = new string[] { "data-src", "src" };
                else
                    fallbackAttrs = new string[] { name };

                foreach (var attr in fallbackAttrs)
                {
                    var val = node.GetAttributeValue(attr, null);
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
                return string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string CleanText(string? input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var s = WebUtility.HtmlDecode(input);
            s = Regex.Replace(s, @"\s+\n", "\n");
            s = Regex.Replace(s, @"\n\s+", "\n");
            s = Regex.Replace(s, @"\s{2,}", " ");
            return s.Trim();
        }

        // Load config từ DB (y chang file cũ)
        public async Task<PageConfigModel?> LoadPageConfig(int id)
        {
            string connStr = "server=localhost;database=code_camp_2025;uid=root;pwd=;";
            using var conn = new MySqlConnection(connStr);
            await conn.OpenAsync();

            var cmd = new MySqlCommand("SELECT * FROM page_config WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PageConfigModel
                {
                    Id = reader.GetInt32("id"),
                    BaseDomain = reader["base_domain"].ToString() ?? "",
                    BaseUrl = reader["base_url"].ToString() ?? "",
                    TourName = reader["tour_name"].ToString() ?? "",
                    TourCode = reader["tour_code"].ToString() ?? "",
                    TourPrice = reader["tour_price"].ToString() ?? "",
                    ImageUrl = reader["image_url"].ToString() ?? "",
                    DepartureLocation = reader["departure_location"].ToString() ?? "",
                    DepartureDate = reader["departure_date"].ToString() ?? "",
                    TourDuration = reader["tour_duration"].ToString() ?? "",
                    PagingType = reader["paging_type"].ToString() ?? "",
                    TourDetailUrl = reader["tour_detail_url"].ToString() ?? "",
                    TourDetailDayTitle = reader["tour_detail_day_title"].ToString() ?? "",
                    TourDetailDayContent = reader["tour_detail_day_content"].ToString() ?? "",
                    TourDetailNote = reader["tour_detail_note"].ToString() ?? "",
                    CrawlType = reader["crawl_type"].ToString() ?? "",
                    TourListSelector = reader["tour_list_selector"].ToString() ?? "",
                    ImageAttr = reader["image_attr"].ToString() ?? "",
                    TourDetailAttr = reader["tour_detail_attr"].ToString() ?? "",
                    LoadMoreButtonSelector = reader["load_more_button_selector"].ToString() ?? "",
                    LoadMoreType = reader["load_more_type"].ToString() ?? ""
                };
            }
            return null;
        }

        private static string ToAbsoluteUrl(string baseDomain, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (url.StartsWith("//")) return "https:" + url;
            if (url.StartsWith("http://") || url.StartsWith("https://")) return url;
            if (string.IsNullOrWhiteSpace(baseDomain)) return url;
            return $"{baseDomain.TrimEnd('/')}/{url.TrimStart('/')}";
        }

    }
}
