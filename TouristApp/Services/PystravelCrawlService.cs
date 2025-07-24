using HtmlAgilityPack;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class PystravelCrawlService
    {
        private readonly HttpClient _httpClient;

        public PystravelCrawlService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<List<PystravelTourModel>> GetToursAsync()
        {
            var tours = new List<PystravelTourModel>();
            var html = await _httpClient.GetStringAsync("https://pystravel.vn/");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var tourNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'drop-shadow-md')]");
            if (tourNodes == null) return tours;

            foreach (var node in tourNodes)
            {
                try
                {
                    var tour = new PystravelTourModel();

                    // 🏷 Tên Tour
                    tour.TourName = node.SelectSingleNode(".//h3")?.InnerText?.Trim();

                    // 🖼 Ảnh
                    var imgNode = node.SelectSingleNode(".//img[contains(@class,'object-cover')]");
                    var imgSrc = imgNode?.GetAttributeValue("src", null);
                    tour.ImageUrl = imgSrc?.StartsWith("http") == true ? imgSrc : $"https://pystravel.vn{imgSrc}";

                    // 🔗 URL chi tiết
                    var detailLink = node.SelectSingleNode(".//a")?.GetAttributeValue("href", null);
                    tour.TourDetailUrl = detailLink?.StartsWith("http") == true ? detailLink : $"https://pystravel.vn{detailLink}";
                    Console.WriteLine($"📍 URL chi tiết: {tour.TourDetailUrl}");


                    // 💰 Giá
                    tour.Price = node.SelectSingleNode(".//div[contains(@class,'text-xl') and contains(@class,'font-bold')]")?.InnerText?.Trim();

                    // ⏱ Thời gian
                    tour.Duration = node.SelectSingleNode(".//div[contains(@class,'flex-1')]//span[contains(@class,'uppercase')]")?.InnerText?.Trim();

                    // 📍 Nơi khởi hành
                    tour.StartingPoint = node.SelectSingleNode(".//span[contains(text(),'Điểm đi:')]")?.InnerText?.Replace("Điểm đi:", "").Trim();
                    Console.WriteLine($"📍 URL chi tiết: {tour.TourDetailUrl}");

                    // 📥 Crawl chi tiết
                    await CrawlTourDetailAsync(tour);

                    tours.Add(tour);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Lỗi tour: {ex.Message}");
                }
            }

            return tours;
        }

        private async Task CrawlTourDetailAsync(PystravelTourModel tour)
        {
         
            if (string.IsNullOrEmpty(tour.TourDetailUrl)) return;
            Console.WriteLine($"🔍 Bắt đầu crawl trang chi tiết: {tour.TourDetailUrl}");


            try
            {
                var detailHtml = await _httpClient.GetStringAsync(tour.TourDetailUrl);
                var doc = new HtmlDocument();
                doc.LoadHtml(detailHtml);

                // 🎯 Lịch trình
                var scheduleBlocks = doc.DocumentNode.SelectNodes("//div[@id='plan']//div[contains(@class,'border-primary-v2')]");
                if (scheduleBlocks != null)
                {
                    foreach (var block in scheduleBlocks)
                    {
                        var titleNode = block.SelectSingleNode(".//h3[contains(@class,'text-lg')]");
                        var contentNode = block.SelectSingleNode(".//div[contains(@class,'tour-detail_tour-content__9q68m')]");

                        if (titleNode != null && contentNode != null)
                        {
                            string title = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());
                            string content = HtmlEntity.DeEntitize(contentNode.InnerText.Trim());

                            tour.Schedule.Add(new ScheduleDay
                            {
                                DayTitle = title,
                                DayContent = content
                            });
                        }
                        else
                        {
                            Console.WriteLine("⚠️ Một khối ngày không có title hoặc nội dung.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ Không tìm thấy khối lịch trình nào.");
                }


                // 📌 Điều khoản
                // 🔍 Tìm khối điều khoản bao gồm và chính sách
                var noteSections = doc.DocumentNode.SelectNodes("//div[@id='includes']//div[contains(@class, 'border') and contains(@class, 'p-6')]");
                if (noteSections != null)
                {
                    foreach (var section in noteSections)
                    {
                        var titleNode = section.SelectSingleNode(".//h3");
                        var listNode = section.SelectSingleNode(".//ul");

                        if (titleNode != null && listNode != null)
                        {
                            var title = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());

                            var items = listNode.SelectNodes(".//li")
                                ?.Select(li => "• " + HtmlEntity.DeEntitize(li.InnerText.Trim()))
                                ?.ToList();

                            if (items != null && items.Count > 0)
                            {
                                tour.ImportantNotes[title] = string.Join("\n", items);
                                Console.WriteLine($"✅ Lấy được ghi chú: {title} ({items.Count} mục)");
                            }
                        }
                        else
                        {
                            Console.WriteLine("⚠️ Một khối điều khoản không có tiêu đề hoặc danh sách.");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ Không tìm thấy khối #includes điều khoản.");
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi chi tiết tour: {ex.Message}");
            }
        }
    }
}
