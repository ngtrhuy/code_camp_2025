using HtmlAgilityPack;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Net;
using System.Text.RegularExpressions;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class SeleniumCrawlService
    {
        private readonly TourRepository _tourRepository;

        public SeleniumCrawlService(TourRepository tourRepository)
        {
            _tourRepository = tourRepository;
        }

        public List<StandardTourModel> CrawlToursWithSelenium(PageConfigModel config, int maxTours)
        {
            if (maxTours <= 0) maxTours = int.MaxValue;

            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(config.BaseUrl);

            // Load more: Bỏ break khi đủ maxTours!
            if (config.PagingType == "load_more")
            {
                while (true)
                {
                    try
                    {
                        var currentDoc = new HtmlDocument();
                        currentDoc.LoadHtml(driver.PageSource);
                        var currentNodes = currentDoc.DocumentNode.SelectNodes(config.TourListSelector);
                        int currentCount = currentNodes?.Count ?? 0;

                        // KHÔNG break khi đủ maxTours
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
                        if (newCount <= currentCount) break; // Không load thêm được tour mới thì dừng
                    }
                    catch { break; }
                }
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(driver.PageSource);
            var tours = new List<StandardTourModel>();
            var nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);

            // Debug selector
            Console.WriteLine("========== DEBUG: Tổng số node tour lấy được: " + (nodes?.Count ?? 0) + " ==========");
            if (nodes == null)
            {
                Console.WriteLine("========== DEBUG: Không lấy được node tour nào. Kiểm tra lại selector hoặc trang web. ==========");
                return tours;
            }

            // Khởi tạo HashSet chống trùng local
            var codeSet = new HashSet<string>();
            var urlSet = new HashSet<string>();
            // nameSet không cần thiết với đa số trang (có thể bỏ nếu muốn)

            foreach (var node in nodes)
            {
                try
                {
                    var tempDeparture = CleanText(GetTextByXPath(node, config.DepartureLocation));
                    var tempDuration = CleanText(GetTextByXPath(node, config.TourDuration));

                    var tour = new StandardTourModel
                    {
                        TourName = CleanText(GetTextByXPath(node, config.TourName)),
                        TourCode = config.TourCode == "NULL" ? "" : CleanText(GetTextOrAttr(node, config.TourCode)),
                        Price = CleanText(GetTextByXPath(node, config.TourPrice)),
                        ImageUrl = GetAttrByXPath(node, config.ImageUrl, config.ImageAttr),
                        TourDetailUrl = GetAttrByXPath(node, config.TourDetailUrl, config.TourDetailAttr),
                        DepartureLocation = tempDeparture,
                        Duration = tempDuration
                    };

                    // Không lưu nếu cả 3 trường đều rỗng (chống bản ghi rác)
                    if (string.IsNullOrWhiteSpace(tour.TourName)
                        && string.IsNullOrWhiteSpace(tour.TourCode)
                        && string.IsNullOrWhiteSpace(tour.TourDetailUrl))
                        continue;

                    if (!string.IsNullOrEmpty(tour.TourDetailUrl) && !tour.TourDetailUrl.StartsWith("http"))
                        tour.TourDetailUrl = config.BaseDomain.TrimEnd('/') + "/" + tour.TourDetailUrl.TrimStart('/');

                    var codeKey = tour.TourCode?.Trim() ?? "";
                    var urlKey = tour.TourDetailUrl?.Trim() ?? "";

                    // Bỏ qua nếu tour đã tồn tại trong DB (chỉ kiểm tra bảng Tour)
                    if (_tourRepository.IsTourExists(codeKey, urlKey))
                        continue;

                    // Chống trùng local trong cùng một lần crawl
                    if (!string.IsNullOrWhiteSpace(codeKey) && codeSet.Contains(codeKey)) continue;
                    if (!string.IsNullOrWhiteSpace(urlKey) && urlSet.Contains(urlKey)) continue;

                    if (!string.IsNullOrWhiteSpace(codeKey)) codeSet.Add(codeKey);
                    if (!string.IsNullOrWhiteSpace(urlKey)) urlSet.Add(urlKey);

                    // Crawl detail nếu có url (giữ logic cũ)
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

                            tour.Schedule = new List<TourScheduleItem>();
                            var dayTitles = detailHtml.DocumentNode.SelectNodes(config.TourDetailDayTitle)?.ToList() ?? new();
                            var dayContents = detailHtml.DocumentNode.SelectNodes(config.TourDetailDayContent)?.ToList() ?? new();

                            for (int i = 0; i < Math.Min(dayTitles.Count, dayContents.Count); i++)
                            {
                                tour.Schedule.Add(new TourScheduleItem
                                {
                                    Id = i + 1,
                                    DayTitle = CleanText(dayTitles[i].InnerText),
                                    DayContent = CleanText(dayContents[i].InnerText)
                                });
                            }

                            tour.ImportantNotes = new Dictionary<string, string>();
                            var noteNodes = detailHtml.DocumentNode.SelectNodes(config.TourDetailNote);
                            if (noteNodes != null && noteNodes.Count > 0)
                            {
                                var notes = string.Join("\n\n", noteNodes.Select(n => CleanText(n.InnerText)));
                                tour.ImportantNotes["Ghi chú"] = notes;
                            }

                            var dateNodes = detailHtml.DocumentNode.SelectNodes(config.DepartureDate);
                            if (dateNodes != null)
                            {
                                tour.DepartureDates = dateNodes
                                    .Select(x => HtmlEntity.DeEntitize(x.InnerText).Trim())
                                    .Where(s => !string.IsNullOrEmpty(s))
                                    .ToList();
                            }

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

                    // Thêm vào list tour mới
                    tours.Add(tour);

                    // Chỉ break khi đã đủ số tour mới (không bị duplicate, không bản ghi rỗng)
                    if (tours.Count >= maxTours) break;
                }
                catch { }
            }

            return tours;
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
    }
}
