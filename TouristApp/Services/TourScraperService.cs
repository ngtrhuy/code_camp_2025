// ✅ FILE: TourScraperService.cs
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class TourScraperService
    {
        public async Task<List<StandardTourModel>> GetToursAsync(string url)
        {
            var result = new List<StandardTourModel>();
            var html = await LoadPageHtmlAsync(url);

            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            var tourNodes = htmlDoc.DocumentNode.SelectNodes("//div[contains(@class, 'bpv-box-item')]");
            if (tourNodes != null)
            {
                foreach (var node in tourNodes)
                {
                    try
                    {
                        var id = node.GetAttributeValue("data-id", "");
                        var titleNode = node.SelectSingleNode(".//a[@class='item-name']");
                        var detailUrl = "https://www.bestprice.vn" + titleNode?.GetAttributeValue("href", "");

                        var route = CleanText(node.SelectSingleNode(".//div[contains(@class,'route')]")?.InnerText);
                        var duration = CleanText(node.SelectSingleNode(".//div[contains(@class,'block-duration')]")?.InnerText);
                        var imgNode = node.SelectSingleNode(".//img");
                        var img = imgNode?.GetAttributeValue("data-src", "") ?? imgNode?.GetAttributeValue("src", "");

                        var web = new HtmlWeb();
                        var detailDoc = web.Load(detailUrl);

                        var tour = ParseTourDetail(detailDoc, detailUrl, id, route, duration, img);
                        result.Add(tour);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }
            return result;
        }

        public async Task<List<StandardTourModel>> GetToursUsingSeleniumAsync(TourSeleniumService seleniumService, string url)
        {
            var result = new List<StandardTourModel>();
            var tourItems = await seleniumService.GetAllTourItemsAsync(url);

            foreach (var item in tourItems)
            {
                try
                {
                    var web = new HtmlWeb();
                    var detailDoc = web.Load(item.Url);

                    var tour = ParseTourDetail(detailDoc, item.Url, "", item.DepartureLocation, item.Duration, item.ImageUrl);
                    result.Add(tour);
                }
                catch
                {
                    continue;
                }
            }

            return result;
        }

        private StandardTourModel ParseTourDetail(HtmlDocument detailDoc, string url, string id = "", string routeFromList = "", string durationFromList = "", string imageFromList = "")
        {
            var name = CleanText(detailDoc.DocumentNode.SelectSingleNode("//h1")?.InnerText);

            var img = !string.IsNullOrEmpty(imageFromList)
                ? imageFromList
                : detailDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'col-img')]//img")?.GetAttributeValue("data-src", "") ??
                  detailDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'col-img')]//img")?.GetAttributeValue("src", "") ??
                  detailDoc.DocumentNode.SelectSingleNode("//img")?.GetAttributeValue("src", "");

            var price = CleanText(detailDoc.DocumentNode.SelectSingleNode("//span[contains(@class,'sale_price')]")?.InnerText);

            var route = !string.IsNullOrEmpty(routeFromList)
                ? routeFromList
                : CleanText(detailDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'route')]")?.InnerText);

            var duration = !string.IsNullOrEmpty(durationFromList)
                ? durationFromList
                : CleanText(detailDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'block-duration')]")?.InnerText);

            var schedule = new List<TourScheduleItem>();
            var itineraryBoxes = detailDoc.DocumentNode.SelectNodes("//div[contains(@class, 'itinerary-box')]");
            if (itineraryBoxes != null)
            {
                foreach (var box in itineraryBoxes)
                {
                    var dayTitle = CleanText(box.SelectSingleNode(".//div[contains(@class, 'iti-day-title')]")?.InnerText);
                    var contentNode = box.SelectSingleNode(".//div[contains(@class, 'itinerary-content')]");
                    if (!string.IsNullOrEmpty(dayTitle) && contentNode != null)
                    {
                        var description = string.Join(" ", contentNode.Descendants()
                            .Where(n => n.Name == "p" || n.Name == "li" || n.Name == "div")
                            .Select(n => CleanText(n.InnerText))
                            .Where(text => !string.IsNullOrWhiteSpace(text)));

                        schedule.Add(new TourScheduleItem
                        {
                            DayTitle = dayTitle,
                            DayContent = description
                        });
                    }
                }
            }

            var importantNotes = new Dictionary<string, string>();
            void ExtractPolicy(string idName, string title)
            {
                var section = detailDoc.DocumentNode.SelectSingleNode($"//div[@id='{idName}']");
                if (section != null)
                {
                    var text = CleanText(section.InnerText);
                    if (!string.IsNullOrWhiteSpace(text))
                        importantNotes[title] = text;
                }
            }

            ExtractPolicy("service_inclusion", "Giá bao gồm");
            ExtractPolicy("service_exclusion", "Không bao gồm");
            ExtractPolicy("cancellation_policy", "Huỷ / Đổi tour");
            ExtractPolicy("children_policy_title", "Trẻ em / Em bé");
            ExtractPolicy("visa_information", "Thông tin Visa");

            var departureDates = new List<string>();
            var departureNodes = detailDoc.DocumentNode.SelectNodes("//ul[contains(@class,'list-depart-date')]/li");
            if (departureNodes != null)
            {
                foreach (var li in departureNodes)
                {
                    var monthMatch = Regex.Match(li.InnerText, @"Tháng\s+(\d+):");
                    if (monthMatch.Success)
                    {
                        var month = int.Parse(monthMatch.Groups[1].Value);
                        var spans = li.SelectSingleNode(".//span")?.InnerText.Split(",").Select(s => s.Trim());
                        if (spans != null)
                        {
                            foreach (var day in spans)
                            {
                                if (int.TryParse(day, out int dayInt))
                                    departureDates.Add($"{dayInt:D2}/{month:D2}");
                            }
                        }
                    }
                }
            }

            return new StandardTourModel
            {
                TourName = name,
                TourCode = id,
                Price = price,
                ImageUrl = img,
                DepartureLocation = route,
                Duration = duration,
                TourDetailUrl = url,
                DepartureDates = departureDates,
                Schedule = schedule,
                ImportantNotes = importantNotes
            };
        }

        private string CleanText(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return Regex.Replace(WebUtility.HtmlDecode(input), @"\s+", " ").Trim();
        }

        private async Task<string> LoadPageHtmlAsync(string url)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                AllowAutoRedirect = true,
                UseCookies = true
            };

            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

            var response = await client.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new Exception("Bị chặn 403 - Bạn cần dùng proxy hoặc bypass Cloudflare");

            return await response.Content.ReadAsStringAsync();
        }
    }
}
