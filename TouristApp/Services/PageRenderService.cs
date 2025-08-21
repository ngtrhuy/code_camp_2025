using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TouristApp.Services;

namespace TouristApp.Services
{
    public class PageRenderService : IPageRenderService
    {
        public async Task<PageRenderResult> RenderAsync(string url, string mode = "server_side",
                string? loadMoreSelector = null, int loadMoreClicks = 0)
        {
            var res = new PageRenderResult { FinalUrl = url, BaseDomain = GetBaseDomain(url) };

            // 🔧 Normalize ngay từ đầu
            var normalizedUrl = NormalizeUrl(url);
            if (!string.Equals(normalizedUrl, url, StringComparison.OrdinalIgnoreCase))
                res.Logs.Add($"Normalized URL: {normalizedUrl}");

            res.FinalUrl = normalizedUrl;
            res.BaseDomain = GetBaseDomain(normalizedUrl);

            if (mode == "client_side")
            {
                await RenderClientAsync(normalizedUrl, res, loadMoreSelector, loadMoreClicks);
                return res;
            }

            if (mode == "server_side")
            {
                await RenderServerAsync(normalizedUrl, res);
                return res;
            }

            // auto
            await RenderServerAsync(normalizedUrl, res);
            if (IsLikelyTooThin(res.Html))
            {
                res.Logs.Add("Auto mode: fallback to client-side rendering.");
                await RenderClientAsync(normalizedUrl, res, loadMoreSelector, loadMoreClicks);
            }
            return res;
        }

        private async Task RenderServerAsync(string url, PageRenderResult res)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
                res.Html = await client.GetStringAsync(url);
                res.RenderModeUsed = "server_side";
                res.Logs.Add("Server-side: fetched HTML via HttpClient.");
            }
            catch (Exception ex)
            {
                res.Logs.Add($"Server-side fetch failed: {ex.Message}");
                res.Html = "";
            }
        }

        private async Task RenderClientAsync(string url, PageRenderResult res, string? loadMoreSelector, int loadMoreClicks)
        {
            var options = new ChromeOptions();
            options.AddArgument("--headless=new");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");

            try
            {
                using var driver = new ChromeDriver(options);
                driver.Navigate().GoToUrl(url);
                await Task.Delay(1500);
                // Nếu có selector → click N lần
                if (!string.IsNullOrWhiteSpace(loadMoreSelector) && loadMoreClicks > 0)
                {
                    for (int i = 0; i < loadMoreClicks; i++)
                    {
                        try
                        {
                            IWebElement btn;
                            var sel = loadMoreSelector.Trim();
                            if (sel.StartsWith("//"))       // XPath
                                btn = driver.FindElement(By.XPath(sel));
                            else if (sel.StartsWith(".") || sel.Contains(" ")) // CSS
                                btn = driver.FindElement(By.CssSelector(sel));
                            else                              // class name ngắn gọn
                                btn = driver.FindElement(By.ClassName(sel.TrimStart('.')));

                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({block:'center'});", btn);
                            btn.Click();
                            await Task.Delay(1200); // đợi trang load thêm
                        }
                        catch { break; }
                    }
                }
                else
                {
                    // Không có nút → thử scroll vài lần để kích hoạt lazy load
                    for (int i = 0; i < 3; i++) { ScrollToBottom(driver); await Task.Delay(800); }
                }
                res.Html = driver.PageSource;
                res.FinalUrl = driver.Url;
                res.BaseDomain = GetBaseDomain(res.FinalUrl);
                res.RenderModeUsed = "client_side";
                res.Logs.Add("Client-side: captured PageSource via Selenium (with load-more if provided).");
                res.Html = driver.PageSource;
                res.FinalUrl = driver.Url;
                res.BaseDomain = GetBaseDomain(res.FinalUrl);
                res.RenderModeUsed = "client_side";
                res.Logs.Add("Client-side: captured PageSource via Selenium.");
            }
            catch (Exception ex)
            {
                res.Logs.Add($"Client-side render failed: {ex.Message}");
                res.Html = "";
            }
        }

        private static string GetBaseDomain(string url)
        {
            var norm = NormalizeUrl(url);
            return Uri.TryCreate(norm, UriKind.Absolute, out var u)
                ? $"{u.Scheme}://{u.Host}"
                : "";
        }

        // ✅ Thêm hàm chuẩn hoá
        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url.Trim();
            url = url.Trim();

            // hỗ trợ dạng //example.com/path
            if (url.StartsWith("//")) return "https:" + url;

            // nếu chưa có scheme thì thêm https://
            var hasScheme = Regex.IsMatch(url, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://");
            if (!hasScheme) return "https://" + url;

            return url;
        }

        private static void ScrollToBottom(ChromeDriver driver)
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");
        }

        private static bool IsLikelyTooThin(string html)
        {
            if (string.IsNullOrEmpty(html)) return true;
            // Heuristic đơn giản: ít hơn 5k ký tự coi như "mỏng"
            return html.Length < 5000;
        }
    }
}
