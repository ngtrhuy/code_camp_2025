using HtmlAgilityPack;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class DeVietTourCrawler
    {
        public async Task<List<DeVietTourInfo>> CrawlToursAsync(string categoryUrl)
        {
            var tourList = new List<DeVietTourInfo>();
            var web = new HtmlWeb();
            var doc = await Task.Run(() => web.Load(categoryUrl));

            var tourNodes = doc.DocumentNode.SelectNodes("//article[contains(@class, 'tour-item')]");
            if (tourNodes == null) return tourList;

            foreach (var node in tourNodes)
            {
                try
                {
                    var titleNode = node.SelectSingleNode(".//h3[contains(@class,'tour-info-tit')]/a");
                    var priceNode = node.SelectSingleNode(".//div[contains(@class,'tour-info-price')]/span");
                    var durationNode = node.SelectSingleNode(".//span[contains(@class,'t2')]");
                    var imageNode = node.SelectSingleNode(".//figure[contains(@class,'tour-img')]/a/img");

                    var detailUrl = titleNode?.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrEmpty(detailUrl)) continue;

                    var tour = new DeVietTourInfo
                    {
                        Title = HtmlEntity.DeEntitize(titleNode?.InnerText?.Trim() ?? ""),
                        Url = detailUrl,
                        Price = priceNode?.InnerText?.Trim() ?? "",
                        Duration = durationNode?.InnerText?.Trim() ?? "",
                        ImageUrl = imageNode?.GetAttributeValue("src", string.Empty) ?? "",
                        Itinerary = new List<TourDay>()
                    };

                    await CrawlTourDetailAsync(tour);
                    tourList.Add(tour);
                }
                catch
                {
                    continue;
                }
            }

            // Lọc trùng theo Title, Price, Duration, DepartureDate
            return tourList
                .GroupBy(t => new { t.Title, t.Price, t.Duration, t.DepartureDate })
                .Select(g => g.First())
                .ToList();
        }

        public async Task CrawlTourDetailAsync(DeVietTourInfo tour)
        {
            var web = new HtmlWeb();
            var doc = await Task.Run(() => web.Load(tour.Url));

            // Hãng hàng không
            var airlineIconNode = doc.DocumentNode.SelectSingleNode("//img[contains(@src, 'airplane.svg')]");
            if (airlineIconNode != null)
            {
                var airlineNode = airlineIconNode.ParentNode.SelectSingleNode(".//strong");
                if (airlineNode != null)
                    tour.Airline = airlineNode.InnerText?.Trim();
            }


            // Ngày khởi hành chính
            var departureNode = doc.DocumentNode.SelectSingleNode("//ul[contains(@class,'tdetail-gen-date')]");
            if (departureNode != null)
            {
                tour.DepartureDate = HtmlEntity.DeEntitize(departureNode.InnerText?.Trim());
            }

            // Lịch trình
            var itineraryDays = doc.DocumentNode.SelectNodes("//div[contains(@class,'accordion lt-acc')]/div");
            if (itineraryDays != null)
            {
                foreach (var day in itineraryDays)
                {
                    var dayTitle = day.SelectSingleNode(".//h3[contains(@class,'s18')]")?.InnerText?.Trim();
                    var dayContentNode = day.SelectSingleNode(".//div[contains(@class,'lichtrinh-content')]");
                    var dayContent = dayContentNode?.InnerText?.Replace("\n", " ").Replace("\r", "").Trim();

                    if (!string.IsNullOrEmpty(dayTitle) && !string.IsNullOrEmpty(dayContent))
                    {
                        tour.Itinerary.Add(new TourDay
                        {
                            DayTitle = HtmlEntity.DeEntitize(dayTitle),
                            DayContent = HtmlEntity.DeEntitize(dayContent)
                        });
                    }
                }
            }

            // Giá chi tiết
            var priceDetailNode = doc.DocumentNode.SelectSingleNode("//span[contains(@class,'t6 sbold s28')]");
            if (priceDetailNode != null)
            {
                tour.TourPriceDetail = HtmlEntity.DeEntitize(priceDetailNode.InnerText?.Trim());
            }

            // Các chính sách khác (giá bao gồm, không bao gồm, trẻ em, hợp đồng,...)
            var contentSections = doc.DocumentNode.SelectNodes("//ul[contains(@class,'tdetail-lcontent')]");
            if (contentSections != null)
            {
                foreach (var section in contentSections)
                {
                    var titleNode = section.SelectSingleNode("./preceding-sibling::*[1]");
                    string sectionTitle = titleNode?.InnerText?.ToLower()?.Trim() ?? "";
                    string textContent = HtmlEntity.DeEntitize(section.InnerText?.Trim());

                    if (sectionTitle.Contains("giá bao gồm") && tour.Included == null)
                        tour.Included = textContent.Replace("\n", " ").Replace("\r", "").Trim();

                    else if (sectionTitle.Contains("không bao gồm") && tour.NotIncluded == null)
                        tour.NotIncluded = textContent.Replace("\n", " ").Replace("\r", "").Trim();

                    else if (sectionTitle.Contains("trẻ em") && tour.ChildrenPolicy == null)
                        tour.ChildrenPolicy = textContent.Replace("\n", " ").Replace("\r", "").Trim();

                    else if ((sectionTitle.Contains("hợp đồng") || sectionTitle.Contains("đặt cọc")) && tour.ContractPolicy == null)
                        tour.ContractPolicy = textContent.Replace("\n", " ").Replace("\r", "").Trim();
                }
            }

            tour.TourDetailUrl = tour.Url;
        }

        public async Task<List<string>> GetAllTourDetailUrlsAsync(string categoryUrl)
        {
            var urls = new List<string>();
            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync(categoryUrl);

            var tourLinks = doc.DocumentNode.SelectNodes("//a[contains(@href, '/tour/')]");
            if (tourLinks != null)
            {
                foreach (var link in tourLinks)
                {
                    var href = link.GetAttributeValue("href", "");
                    if (!string.IsNullOrWhiteSpace(href) &&
                        Uri.IsWellFormedUriString(href, UriKind.Absolute) &&
                        !urls.Contains(href))
                    {
                        urls.Add(href);
                    }
                }
            }

            return urls.Distinct().ToList();
        }
    }
}
