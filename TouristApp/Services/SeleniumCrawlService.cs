using HtmlAgilityPack;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.XPath;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class SeleniumCrawlService
    {
        public List<StandardTourModel> CrawlToursWithSelenium(PageConfigModel config)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(config.BaseUrl);

            if (config.PagingType == "load_more")
            {
                while (true)
                {
                    try
                    {
                        var currentHtml = driver.PageSource;
                        var currentDoc = new HtmlDocument();
                        currentDoc.LoadHtml(currentHtml);

                        HtmlNodeCollection? currentNodes = null;
                        try
                        {
                            Console.WriteLine("👉 XPath đang dùng: " + config.TourListSelector);
                            currentNodes = currentDoc.DocumentNode.SelectNodes(config.TourListSelector);
                        }
                        catch (XPathException ex)
                        {
                            Console.WriteLine($"❌ XPath lỗi khi load more: {ex.Message}");
                            break;
                        }

                        int currentItemCount = currentNodes?.Count ?? 0;

                        if (currentItemCount >= 20)
                        {
                            Console.WriteLine("✅ Đã đủ 20 tour, dừng load thêm.");
                            break;
                        }

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

                        if (loadMoreButton == null)
                        {
                            Console.WriteLine("🛑 Không tìm thấy nút Load More.");
                            break;
                        }

                        IJavaScriptExecutor js = (IJavaScriptExecutor)driver;
                        js.ExecuteScript("arguments[0].click();", loadMoreButton);
                        Thread.Sleep(7000);

                        var newHtml = driver.PageSource;
                        var newDoc = new HtmlDocument();
                        newDoc.LoadHtml(newHtml);

                        HtmlNodeCollection? newNodes = null;
                        try
                        {
                            newNodes = newDoc.DocumentNode.SelectNodes(config.TourListSelector);
                        }
                        catch (XPathException ex)
                        {
                            Console.WriteLine($"❌ XPath lỗi sau khi load more: {ex.Message}");
                            break;
                        }

                        int newItemCount = newNodes?.Count ?? 0;

                        if (newItemCount <= currentItemCount)
                        {
                            Console.WriteLine("⚠️ Không có thêm dữ liệu sau khi click.");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Lỗi khi click Load More (JS): {ex.Message}");
                        break;
                    }
                }
            }

            var html = driver.PageSource;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var tours = new List<StandardTourModel>();
            HtmlNodeCollection? nodes = null;

            try
            {
                nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);
            }
            catch (XPathException ex)
            {
                Console.WriteLine($"❌ XPath selector lỗi: {ex.Message}");
                return tours;
            }

            if (nodes == null)
            {
                Console.WriteLine("⚠️ Không tìm thấy tour nào.");
                return tours;
            }

            foreach (var node in nodes)
            {
                if (tours.Count >= 20) break;
                try
                {
                    var tour = new StandardTourModel
                    {
                        TourName = CleanText(GetTextByXPath(node, config.TourName)),
                        TourCode = CleanText(GetTextOrAttr(node, config.TourCode)),
                        Price = CleanText(GetTextByXPath(node, config.TourPrice)),
                        ImageUrl = GetAttrByXPath(node, config.ImageUrl, config.ImageAttr),
                        TourDetailUrl = GetAttrByXPath(node, config.TourDetailUrl, config.TourDetailAttr)
                    };

                    if (!string.IsNullOrEmpty(tour.TourDetailUrl) && !tour.TourDetailUrl.StartsWith("http"))
                    {
                        tour.TourDetailUrl = config.BaseDomain.TrimEnd('/') + "/" + tour.TourDetailUrl.TrimStart('/');
                    }

                    if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                    {
                        try
                        {
                            var detailHtml = new HtmlWeb().Load(tour.TourDetailUrl);

                            var dayTitles = detailHtml.DocumentNode.SelectNodes(config.TourDetailDayTitle)?.ToList() ?? new();
                            var dayContents = detailHtml.DocumentNode.SelectNodes(config.TourDetailDayContent)?.ToList() ?? new();

                            for (int i = 0; i < Math.Min(dayTitles.Count, dayContents.Count); i++)
                            {
                                tour.Schedule.Add(new TourScheduleItem
                                {
                                    DayTitle = CleanText(dayTitles[i].InnerText),
                                    DayContent = CleanText(dayContents[i].InnerText)
                                });
                            }

                            var noteNode = detailHtml.DocumentNode.SelectSingleNode(config.TourDetailNote);
                            if (noteNode != null)
                            {
                                tour.ImportantNotes["Ghi chú"] = CleanText(noteNode.InnerText);
                            }

                            tour.DepartureLocation = CleanText(detailHtml.DocumentNode.SelectSingleNode(config.DepartureLocation)?.InnerText ?? "");
                            tour.Duration = CleanText(detailHtml.DocumentNode.SelectSingleNode(config.TourDuration)?.InnerText ?? "");

                            var dateNodes = detailHtml.DocumentNode.SelectNodes(config.DepartureDate);
                            if (dateNodes != null)
                            {
                                tour.DepartureDates = dateNodes.Select(x => CleanText(x.InnerText)).ToList();
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Không thể crawl chi tiết tour: {ex.Message}");
                        }
                    }

                    tours.Add(tour);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Lỗi khi phân tích tour: {ex.Message}");
                }
            }

            return tours;
        }

        private string GetTextByXPath(HtmlNode node, string xpath)
        {
            if (string.IsNullOrWhiteSpace(xpath)) return "";
            var raw = node.SelectSingleNode(xpath)?.InnerText ?? "";
            return CleanText(raw);
        }

        private string GetTextOrAttr(HtmlNode node, string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return "";
            return expr.StartsWith("@")
                ? node.GetAttributeValue(expr.Replace("@", ""), "")
                : CleanText(node.SelectSingleNode(expr)?.InnerText ?? "");
        }

        private string GetAttrByXPath(HtmlNode node, string xpath, string attr)
        {
            if (string.IsNullOrWhiteSpace(xpath)) return "";

            var imgNode = node.SelectSingleNode(xpath);
            if (imgNode == null) return "";

            string value = imgNode.GetAttributeValue(attr ?? "src", "");

            if (string.IsNullOrEmpty(value) && attr != "src")
            {
                value = imgNode.GetAttributeValue("src", "");
            }

            return value;
        }

        private string CleanText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            string decoded = WebUtility.HtmlDecode(input);
            string noBreaks = decoded.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            string cleaned = Regex.Replace(noBreaks, @"\s{2,}", " ");
            cleaned = cleaned.Replace(" / ", " ").Replace("/", " ");
            return cleaned.Trim();
        }
    }
}
