using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace TouristApp.Services
{
    public class TourListItem
    {
        public string Url { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string DepartureLocation { get; set; } = "";
        public string Duration { get; set; } = "";
    }

    public class TourSeleniumService
    {
        public async Task<List<TourListItem>> GetAllTourItemsAsync(string url, int maxTours = 400)
        {
            var result = new List<TourListItem>();

            var options = new ChromeOptions();
            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(url);

            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
            wait.Until(d => d.FindElements(By.CssSelector(".bpv-box-item, li.bpv-list-item.tour-ids")).Count > 0);

            int retryCount = 0;
            int clickCount = 0;

            while (true)
            {
                var currentCount = driver.FindElements(By.CssSelector(".bpv-box-item, li.bpv-list-item.tour-ids")).Count;

                if (currentCount >= maxTours)
                    break;

                var button = driver.FindElements(By.CssSelector(".btn-more-tour:not([id='btn_more_selected_tour'])")).FirstOrDefault();
                if (button == null || !button.Displayed || !button.Enabled)
                    break;

                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", button);
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", button);
                clickCount++;

                await Task.Delay(2500);

                var newCount = driver.FindElements(By.CssSelector(".bpv-box-item, li.bpv-list-item.tour-ids")).Count;
                if (newCount == currentCount)
                {
                    retryCount++;
                    if (retryCount >= 3) break;
                }
                else
                {
                    retryCount = 0;
                }
            }

            var tourNodes = driver.FindElements(By.CssSelector(".bpv-box-item, li.bpv-list-item.tour-ids"));
            var baseUri = new Uri(url);

            foreach (var node in tourNodes.Take(maxTours))
            {
                var item = new TourListItem();

                try
                {
                    var linkNode = node.FindElement(By.CssSelector("a.item-name"));
                    item.Url = linkNode.GetAttribute("href");
                }
                catch
                {
                    try
                    {
                        var imgDiv = node.FindElement(By.CssSelector(".col-img"));
                        item.Url = imgDiv.GetAttribute("data-go-url");
                    }
                    catch { }
                }

                try
                {
                    var img = node.FindElement(By.CssSelector("img"));
                    item.ImageUrl = img.GetAttribute("data-src") ?? img.GetAttribute("src") ?? "";
                }
                catch { }

                try
                {
                    item.DepartureLocation = node.FindElement(By.CssSelector(".route"))?.Text ?? "";
                }
                catch { }

                try
                {
                    item.Duration = node.FindElement(By.CssSelector(".block-duration"))?.Text ?? "";
                }
                catch { }

                if (!string.IsNullOrEmpty(item.Url))
                {
                    if (!item.Url.StartsWith("http"))
                        item.Url = $"{baseUri.Scheme}://{baseUri.Host}{item.Url}";

                    result.Add(item);
                }
            }

            return result.DistinctBy(i => i.Url).ToList();
        }
    }
}
