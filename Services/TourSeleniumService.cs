using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace TouristApp.Services
{
    public class TourSeleniumService
    {
        public async Task<List<string>> GetAllTourUrlsAsync(string url)
        {
            var urls = new List<string>();

            var options = new ChromeOptions();
            options.AddArgument("--headless");  // chạy ngầm
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            using var driver = new ChromeDriver(options);
            driver.Navigate().GoToUrl(url);

            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
            wait.Until(d => d.FindElements(By.CssSelector(".bpv-box-item")).Count > 0);

            int maxRetry = 3;
            int retryCount = 0;

            while (true)
            {
                var before = driver.FindElements(By.CssSelector(".bpv-box-item")).Count;

                try
                {
                    var button = wait.Until(d =>
                    {
                        var element = d.FindElement(By.CssSelector(".btn-more-tour"));
                        return (element.Displayed && element.Enabled) ? element : null;
                    });

                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", button);
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", button);

                    await Task.Delay(1500); // chờ load thêm dữ liệu
                }
                catch (WebDriverTimeoutException)
                {
                    break; // không còn nút
                }

                var after = driver.FindElements(By.CssSelector(".bpv-box-item")).Count;

                if (after == before)
                {
                    retryCount++;
                }
                else
                {
                    retryCount = 0;
                }

                if (retryCount >= maxRetry)
                    break;
            }

            // ✅ Lấy danh sách URL tour
            var tourNodes = driver.FindElements(By.CssSelector(".bpv-box-item a.item-name"));
            foreach (var node in tourNodes)
            {
                var href = node.GetAttribute("href");
                if (!string.IsNullOrEmpty(href))
                {
                    if (!href.StartsWith("http"))
                        href = "https://www.bestprice.vn" + href;
                    urls.Add(href);
                }
            }

            return urls;
        }
    }
}
