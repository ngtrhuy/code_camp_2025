using HtmlAgilityPack;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class DeVietTourCrawler
    {
        public async Task<List<StandardTourModel>> CrawlToursAsync(string categoryUrl)
        {
            var tourList = new List<StandardTourModel>();
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

                    var tour = new StandardTourModel
                    {
                        TourName = HtmlEntity.DeEntitize(titleNode?.InnerText?.Trim() ?? ""),
                        Price = priceNode?.InnerText?.Trim() ?? "",
                        Duration = durationNode?.InnerText?.Trim() ?? "",
                        ImageUrl = imageNode?.GetAttributeValue("src", string.Empty) ?? "",
                        TourDetailUrl = detailUrl
                    };

                    await CrawlTourDetailAsync(tour);
                    tourList.Add(tour);
                }
                catch
                {
                    continue;
                }
            }

            return tourList;
        }

        public async Task CrawlTourDetailAsync(StandardTourModel tour)
        {
            var web = new HtmlWeb();
            var doc = await Task.Run(() => web.Load(tour.TourDetailUrl));

            // Ngày khởi hành

            var dateList = doc.DocumentNode.SelectNodes("//ul[@class='tdetail-date']/li");
            if (dateList != null)
            {
                foreach (var li in dateList)
                {
                    var text = li.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(text))
                        tour.DepartureDates.Add(HtmlEntity.DeEntitize(text));
                }
            }

            // Lịch trình từng ngày
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
                        tour.Schedules.Add(new TourScheduleItem
                        {
                            Id = tour.Schedules.Count + 1,
                            DayTitle = HtmlEntity.DeEntitize(dayTitle),
                            DayContent = HtmlEntity.DeEntitize(dayContent)
                        });
                    }
                }
            }

            // Các chú ý/điều khoản (ImportantNotes)
            var contentSections = doc.DocumentNode.SelectNodes("//ul[contains(@class,'tdetail-lcontent')]");
            if (contentSections != null)
            {
                foreach (var section in contentSections)
                {
                    var titleNode = section.SelectSingleNode("./preceding-sibling::*[1]");
                    string sectionTitle = titleNode?.InnerText?.Trim() ?? "";
                    string textContent = HtmlEntity.DeEntitize(section.InnerText?.Replace("\n", " ").Replace("\r", "").Trim() ?? "");

                    if (!string.IsNullOrEmpty(sectionTitle) && !tour.ImportantNotes.ContainsKey(sectionTitle))
                    {
                        tour.ImportantNotes[sectionTitle] = textContent;
                    }
                }
            }
        }
    }
}
