using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using HtmlAgilityPack;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class SeleniumCrawlService
    {
        public List<StandardTourModel> CrawlToursWithSelenium(PageConfigModel config)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless"); // chạy ẩn
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(config.BaseUrl);

            var tours = new List<StandardTourModel>();

            int maxLoad = 10;
            int loadCount = 0;

            while (loadCount < maxLoad)
            {
                try
                {
                    // Scroll xuống cuối trang để load nội dung nếu có lazy load
                    ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
                    Thread.Sleep(2000); // đợi JS tải xong

                    // Nếu có nút "Trang sau" hoặc "Xem thêm", click
                    var nextButton = driver.FindElements(By.XPath("//a[contains(text(),'Sau')]")).FirstOrDefault();
                    if (nextButton != null && nextButton.Displayed)
                    {
                        nextButton.Click();
                        Thread.Sleep(2000);
                    }
                    else
                    {
                        break;
                    }

                    loadCount++;
                }
                catch
                {
                    break;
                }
            }

            // Sau khi load đủ trang → lấy HTML
            var html = driver.PageSource;
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);
            if (nodes == null) return tours;

            foreach (var node in nodes)
            {
                try
                {
                    var tour = new StandardTourModel
                    {
                        TourName = node.SelectSingleNode(config.TourName)?.InnerText.Trim() ?? "",
                        TourCode = node.SelectSingleNode(config.TourCode)?.InnerText.Trim() ?? "",
                        Price = node.SelectSingleNode(config.TourPrice)?.InnerText.Trim() ?? "",
                        ImageUrl = node.SelectSingleNode(config.ImageUrl)?.GetAttributeValue(config.ImageAttr, "") ?? "",
                        DepartureLocation = node.SelectSingleNode(config.DepartureLocation)?.InnerText.Trim() ?? "",
                        DepartureDates = node.SelectNodes(config.DepartureDate)?.Select(x => x.InnerText.Trim()).ToList() ?? new(),
                        Duration = node.SelectSingleNode(config.TourDuration)?.InnerText.Trim() ?? "",
                        TourDetailUrl = node.SelectSingleNode(config.TourDetailUrl)?.GetAttributeValue(config.TourDetailAttr, "") ?? ""
                    };

                    // TODO: Crawl chi tiết nếu cần (có thể gọi lại HtmlWeb hoặc Selenium)
                    tours.Add(tour);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Lỗi khi parse tour: {ex.Message}");
                }
            }

            return tours;
        }
    }
}
