using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using TouristApp.Models;
using static TouristApp.Models.TourModel;

namespace TouristApp.Services
{
    public class TourScraperService
    {
        public async Task<List<TourModel>> GetToursAsync(string url)
        {
            var result = new List<TourModel>();
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

                        var web = new HtmlWeb();
                        var detailDoc = web.Load(detailUrl);

                        var tour = ParseTourDetail(detailDoc, detailUrl, id);
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

        public async Task<List<TourModel>> GetToursUsingSeleniumAsync(TourSeleniumService seleniumService, string url)
        {
            var result = new List<TourModel>();
            var urls = await seleniumService.GetAllTourUrlsAsync(url);

            foreach (var tourUrl in urls)
            {
                try
                {
                    var web = new HtmlWeb();
                    var detailDoc = web.Load(tourUrl);

                    var tour = ParseTourDetail(detailDoc, tourUrl);
                    result.Add(tour);
                }
                catch
                {
                    continue;
                }
            }
            return result;
        }

        private TourModel ParseTourDetail(HtmlDocument detailDoc, string url, string id = "")
        {
            var name = CleanText(detailDoc.DocumentNode.SelectSingleNode("//h1")?.InnerText);

            // ✅ Lấy ảnh từ data-src hoặc src
            var imgNode = detailDoc.DocumentNode.SelectSingleNode("//img[contains(@class,'img-fluid')]")
                          ?? detailDoc.DocumentNode.SelectSingleNode("//img");
            var img = imgNode?.GetAttributeValue("data-src", "") ?? imgNode?.GetAttributeValue("src", "");

            var route = CleanText(detailDoc.DocumentNode.SelectSingleNode("//div[@class='route']")?.InnerText);
            var duration = CleanText(detailDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'block-duration')]")?.InnerText);
            var priceOld = CleanText(detailDoc.DocumentNode.SelectSingleNode("//div[contains(@class,'price-origin')]")?.InnerText);
            var priceNew = CleanText(detailDoc.DocumentNode.SelectSingleNode("//span[contains(@class,'sale_price')]")?.InnerText);

            var reviewScore = CleanText(detailDoc.DocumentNode.SelectSingleNode("//span[contains(@class,'score_review')]")?.InnerText);
            var reviewText = CleanText(detailDoc.DocumentNode.SelectSingleNode("//span[contains(@class,'text-excellent')]")?.InnerText);
            var reviewCount = CleanText(detailDoc.DocumentNode.SelectSingleNode("//span[contains(@class,'total_review')]")?.InnerText);

            var promotion = CleanText(detailDoc.DocumentNode.SelectSingleNode("//p[contains(@class,'text-special')]")?.InnerText);
            var gift = CleanText(detailDoc.DocumentNode.SelectSingleNode("//p[contains(@class,'text-promotion-free')]")?.InnerText);

            // Lấy Itinerary
            var itinerary = new List<TourItineraryItem>();
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

                        itinerary.Add(new TourItineraryItem
                        {
                            DayTitle = dayTitle,
                            Description = description
                        });
                    }
                }
            }

            // Service Policies
            var servicePolicies = new Dictionary<string, List<string>>();
            void ExtractPolicy(string idName, string title)
            {
                var section = detailDoc.DocumentNode.SelectSingleNode($"//div[@id='{idName}']");
                if (section != null)
                {
                    var items = section.SelectNodes(".//li | .//td")
                         ?.Select(n => CleanText(n.InnerText))
                         .Where(text => !string.IsNullOrWhiteSpace(text))
                         .ToList();

                    if (items != null && items.Count > 0)
                        servicePolicies[title] = items;
                    else
                    {
                        var text = CleanText(section.InnerText);
                        if (!string.IsNullOrWhiteSpace(text))
                            servicePolicies[title] = new List<string> { text };
                    }
                }
            }
            ExtractPolicy("service_inclusion", "Giá bao gồm");
            ExtractPolicy("service_exclusion", "Không bao gồm");
            ExtractPolicy("cancellation_policy", "Huỷ / Đổi tour");
            ExtractPolicy("children_policy_title", "Trẻ em / Em bé");
            ExtractPolicy("visa_information", "Thông tin Visa");

            // Departure Dates
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

            return new TourModel
            {
                Id = id,
                Name = name,
                Url = url,
                ImageUrl = img,
                Route = route,
                Duration = duration,
                PriceOld = priceOld,
                PriceNew = priceNew,
                ReviewScore = reviewScore,
                ReviewText = reviewText,
                ReviewCount = reviewCount,
                Promotion = promotion,
                Gift = gift,
                Itinerary = itinerary,
                ServicePolicies = servicePolicies,
                DepartureDates = departureDates
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
