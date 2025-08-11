using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MySqlConnector;
using TouristApp.Models;

namespace TouristApp.Services
{
    public class GenericCrawlServiceServerSide : IGenericCrawlService
    {
        private readonly string _connStr = "server=localhost;database=code_camp_2025;uid=root;pwd=;";

        string SafeGetString(MySqlDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
        }

        public async Task<List<StandardTourModel>> CrawlFromPageConfigAsync(int configId)
        {
            var config = await LoadPageConfig(configId);
            if (config == null) return new List<StandardTourModel>();

            List<StandardTourModel> tours = new();

            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

            var baseUrls = config.BaseUrl
                .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(url => url.Trim().TrimEnd('/'))
                .ToList();

            foreach (var baseUrl in baseUrls)
            {
                int currentPage = 1;
                bool hasMore = true;

                while (hasMore)
                {
                    string pageUrl = baseUrl;

                    if (config.PagingType == "querystring")
                    {
                        pageUrl += pageUrl.Contains("?") ? $"&page={currentPage}" : $"?page={currentPage}";
                    }

                    Console.WriteLine($"🔍 Trang {currentPage} URL: {pageUrl}");

                    var html = await client.GetStringAsync(pageUrl);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);
                    Console.WriteLine($"📄 Số lượng tour ở page {currentPage}: {nodes?.Count ?? 0}");

                    if (nodes == null || nodes.Count == 0)
                    {
                        hasMore = false;
                        break;
                    }

                    foreach (var node in nodes)
                    {
                        try
                        {
                            var tour = new StandardTourModel
                            {
                                TourName = GetText(node, config.TourName),
                                TourCode = GetText(node, config.TourCode),
                                Price = GetText(node, config.TourPrice),
                                ImageUrl = GetAttribute(node, config.ImageUrl, config.ImageAttr),
                                DepartureLocation = GetText(node, config.DepartureLocation),
                                DepartureDates = GetMultipleTexts(node, config.DepartureDate),
                                Duration = GetText(node, config.TourDuration),
                                TourDetailUrl = GetAttribute(node, config.TourDetailUrl, config.TourDetailAttr),
                            };

                            tours.Add(tour);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Lỗi parse tour ở page {currentPage}: {ex.Message}");
                        }
                    }

                    currentPage++;
                    if (config.PagingType != "querystring")
                        break;
                }
            }

            foreach (var tour in tours)
            {
                if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                {
                    var fullUrl = tour.TourDetailUrl.StartsWith("http")
                        ? tour.TourDetailUrl
                        : $"{config.BaseDomain.TrimEnd('/')}/{tour.TourDetailUrl.TrimStart('/')}";

                    await CrawlDetailWithHtmlAgilityPackAsync(tour, fullUrl, config);
                }
            }

            return tours;
        }

        private string GetText(HtmlNode node, string xpath) =>
            node.SelectSingleNode(xpath)?.InnerText.Trim() ?? string.Empty;

        private string GetAttribute(HtmlNode node, string xpath, string attr) =>
            node.SelectSingleNode(xpath)?.GetAttributeValue(attr, "") ?? string.Empty;

        private List<string> GetMultipleTexts(HtmlNode node, string xpath) =>
            node.SelectNodes(xpath)?.Select(n => n.InnerText.Trim()).ToList() ?? new List<string>();

        private async Task CrawlDetailWithHtmlAgilityPackAsync(StandardTourModel tour, string url, PageConfigModel config)
        {
            try
            {
                var doc = await new HtmlWeb().LoadFromWebAsync(url);
                ParseTourDetailFromHtml(doc, tour, config);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Lỗi crawl chi tiết (HTML): {ex.Message}");
            }
        }

        private void ParseTourDetailFromHtml(HtmlDocument doc, StandardTourModel tour, PageConfigModel config)
        {
            // ========= LỊCH TRÌNH =========
            var days = doc.DocumentNode.SelectNodes(config.TourDetailDayTitle);
            var contents = doc.DocumentNode.SelectNodes(config.TourDetailDayContent);

            if (days != null && contents != null && days.Count == contents.Count)
            {
                for (int i = 0; i < days.Count; i++)
                {
                    tour.Schedule.Add(new TourScheduleItem
                    {
                        DayTitle = HtmlEntity.DeEntitize(days[i].InnerText.Trim()),
                        DayContent = HtmlEntity.DeEntitize(contents[i].InnerText.Trim())
                    });
                }
            }

            // ========= IMPORTANT NOTES (theo UL/LI từng heading) =========

            // Tiêu đề hiển thị
            var displayTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "dich vu bao gom", "DỊCH VỤ BAO GỒM" },
                { "dich vu khong bao gom", "DỊCH VỤ KHÔNG BAO GỒM" },
                { "chi phi tre em", "CHI PHÍ TRẺ EM" },
                { "ky hop dong & dat coc tour", "KÝ HỢP ĐỒNG & ĐẶT CỌC TOUR" },
                { "quy dinh huy tour", "QUY ĐỊNH HỦY TOUR" },
            };

            // => canonical hóa heading
            string Canon(string? raw)
            {
                var x = ToAsciiLower(CleanText(raw));
                x = Regex.Replace(x, @"[:.\s]+$", "");

                // từ khóa nhận diện
                string[][] keys =
                {
                    new[] { "dich vu bao gom","gia bao gom","bao gom","included","include","tour bao gom" },
                    new[] { "dich vu khong bao gom","gia khong bao gom","khong bao gom","not included","exclude","chua bao gom" },
                    new[] { "chi phi tre em","chinh sach tre em","tre em" },
                    new[] { "ky hop dong & dat coc tour","ky hop dong","dat coc tour","dat coc","thanh toan & dat coc" },
                    new[] { "quy dinh huy tour","dieu kien huy","chinh sach huy","huy tour","phi huy" },
                };
                string[] canons = {
                    "dich vu bao gom","dich vu khong bao gom","chi phi tre em",
                    "ky hop dong & dat coc tour","quy dinh huy tour"
                };

                for (int i = 0; i < keys.Length; i++)
                    if (keys[i].Any(k => x.Contains(ToAsciiLower(k)))) return canons[i];

                return string.Empty;
            }

            // bucket: canonical -> set items để khử trùng lặp
            var bucket = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            void Merge(string canon, IEnumerable<string> items)
            {
                if (string.IsNullOrWhiteSpace(canon)) return;
                if (!displayTitles.ContainsKey(canon)) return;
                if (!bucket.TryGetValue(canon, out var set))
                    bucket[canon] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var it in items)
                {
                    var t = CleanText(it);
                    if (!string.IsNullOrWhiteSpace(t)) set.Add(t);
                }
            }

            bool IsHeadingLike(HtmlNode n)
            {
                if (n.NodeType != HtmlNodeType.Element) return false;
                var name = n.Name.ToLowerInvariant();
                if (name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "strong" or "b")
                    return true;

                // <p><strong>…</strong></p>
                if (name == "p")
                {
                    var strong = n.SelectSingleNode("./strong|./b");
                    if (strong != null && CleanText(n.InnerText) == CleanText(strong.InnerText))
                        return true;
                }
                return false;
            }

            IEnumerable<string> LiTexts(HtmlNode listNode)
            {
                var lis = listNode.SelectNodes(".//li");
                if (lis == null || lis.Count == 0)
                {
                    var t = CleanText(listNode.InnerText);
                    return string.IsNullOrWhiteSpace(t) ? Array.Empty<string>() : new[] { t };
                }
                return lis.Select(li => CleanText(li.InnerText)).Where(s => !string.IsNullOrWhiteSpace(s));
            }

            void WalkContainer(HtmlNode container)
            {
                string? currentCanon = null;

                foreach (var child in container.ChildNodes.Where(x => x.NodeType == HtmlNodeType.Element))
                {
                    if (IsHeadingLike(child))
                    {
                        var headingText = child.Name.Equals("p", StringComparison.OrdinalIgnoreCase)
                            ? (child.SelectSingleNode("./strong|./b")?.InnerText ?? child.InnerText)
                            : child.InnerText;

                        currentCanon = Canon(headingText);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(currentCanon)) continue;

                    var tag = child.Name.ToLowerInvariant();
                    if (tag is "ul" or "ol")
                    {
                        Merge(currentCanon, LiTexts(child));
                    }
                    else
                    {
                        // UL/OL lồng trong div/section
                        var lists = child.SelectNodes(".//ul|.//ol");
                        if (lists != null)
                        {
                            var tmp = new List<string>();
                            foreach (var l in lists) tmp.AddRange(LiTexts(l));
                            if (tmp.Count > 0) Merge(currentCanon, tmp);
                        }
                    }
                }
            }

            // Tìm container theo thứ tự ưu tiên: config -> Lửa Việt -> DeViet
            var containerCandidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(config.TourDetailNote))
                containerCandidates.Add(config.TourDetailNote);

            // Luaviet
            containerCandidates.Add("//div[contains(concat(' ', normalize-space(@class), ' '), ' editor ') and contains(concat(' ', normalize-space(@class), ' '), ' cms-content ')]");
            // DeViet
            containerCandidates.Add("//div[contains(@class,'tdetail-pitem-wrap')]");

            HtmlNode? foundContainer = null;
            foreach (var xp in containerCandidates.Distinct())
            {
                var n = doc.DocumentNode.SelectSingleNode(xp);
                if (n != null) { foundContainer = n; break; }
            }

            if (foundContainer != null)
            {
                WalkContainer(foundContainer);
            }
            else
            {
                // Fallback: quét toàn trang theo heading -> UL/OL kế tiếp
                var heads = doc.DocumentNode.SelectNodes("//*[self::h1 or self::h2 or self::h3 or self::h4 or self::h5 or self::h6 or self::strong or self::b or (self::p and (./strong or ./b))]");
                if (heads != null)
                {
                    foreach (var h in heads)
                    {
                        var headingText = h.Name.Equals("p", StringComparison.OrdinalIgnoreCase)
                            ? h.SelectSingleNode("./strong|./b")?.InnerText ?? h.InnerText
                            : h.InnerText;

                        var canon = Canon(headingText);
                        if (string.IsNullOrWhiteSpace(canon)) continue;

                        // lấy tất cả UL/OL sau heading cho tới heading kế tiếp (cùng cấp container)
                        var items = new List<string>();
                        var sib = h.NextSibling;
                        while (sib != null)
                        {
                            if (sib.NodeType == HtmlNodeType.Element && IsHeadingLike(sib)) break;

                            if (sib.NodeType == HtmlNodeType.Element)
                            {
                                var tag = sib.Name.ToLowerInvariant();
                                if (tag is "ul" or "ol")
                                    items.AddRange(LiTexts(sib));
                                else
                                {
                                    var lists = sib.SelectNodes(".//ul|.//ol");
                                    if (lists != null)
                                        foreach (var l in lists) items.AddRange(LiTexts(l));
                                }
                            }

                            sib = sib.NextSibling;
                        }

                        Merge(canon, items);
                    }
                }
            }

            // Build kết quả HTML UL theo đúng tiêu đề hiển thị
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in displayTitles)
            {
                if (!bucket.TryGetValue(kv.Key, out var items) || items.Count == 0) continue;
                var ul = "<ul>" + string.Join("", items.Select(li => $"<li>{WebUtility.HtmlEncode(li)}</li>")) + "</ul>";
                result[kv.Value] = ul;
            }

            tour.ImportantNotes = result;

            // ========= NGÀY KHỞI HÀNH (fallback) =========
            if (tour.DepartureDates == null || tour.DepartureDates.Count == 0)
            {
                var dateNodes = doc.DocumentNode.SelectNodes("//ul[contains(@class, 'tdetail-date')]/li");
                if (dateNodes != null)
                {
                    var extractedDates = dateNodes
                        .Select(li => li.InnerText.Trim())
                        .Where(date => !string.IsNullOrWhiteSpace(date))
                        .Select(date => Regex.Match(date, @"\d{1,2}/\d{1,2}").Value)
                        .Where(date => !string.IsNullOrWhiteSpace(date))
                        .Distinct()
                        .ToList();

                    tour.DepartureDates = extractedDates;
                    Console.WriteLine($"📅 Lấy {extractedDates.Count} ngày khởi hành từ ul.tdetail-date cho tour: {tour.TourName}");
                }
            }
        }

        public async Task<PageConfigModel?> LoadPageConfig(int id)
        {
            using var conn = new MySqlConnection(_connStr);
            await conn.OpenAsync();

            var cmd = new MySqlCommand("SELECT * FROM page_config WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new PageConfigModel
                {
                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                    BaseDomain = SafeGetString(reader, "base_domain"),
                    BaseUrl = SafeGetString(reader, "base_url"),
                    TourName = SafeGetString(reader, "tour_name"),
                    TourCode = SafeGetString(reader, "tour_code"),
                    TourPrice = SafeGetString(reader, "tour_price"),
                    ImageUrl = SafeGetString(reader, "image_url"),
                    DepartureLocation = SafeGetString(reader, "departure_location"),
                    DepartureDate = SafeGetString(reader, "departure_date"),
                    TourDuration = SafeGetString(reader, "tour_duration"),
                    TourDetailUrl = SafeGetString(reader, "tour_detail_url"),
                    TourDetailDayTitle = SafeGetString(reader, "tour_detail_day_title"),
                    TourDetailDayContent = SafeGetString(reader, "tour_detail_day_content"),
                    TourDetailNote = SafeGetString(reader, "tour_detail_note"),
                    CrawlType = SafeGetString(reader, "crawl_type"),
                    TourListSelector = SafeGetString(reader, "tour_list_selector"),
                    ImageAttr = SafeGetString(reader, "image_attr"),
                    TourDetailAttr = SafeGetString(reader, "tour_detail_attr"),
                    LoadMoreButtonSelector = SafeGetString(reader, "load_more_button_selector"),
                    LoadMoreType = SafeGetString(reader, "load_more_type"),
                    PagingType = SafeGetString(reader, "paging_type")
                };
            }

            return null;
        }

        // ================== Helpers chung ==================
        private static string ToAsciiLower(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var norm = CleanText(s).Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in norm)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        private static string CleanText(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var t = HtmlEntity.DeEntitize(s);
            t = Regex.Replace(t, @"\s+", " "); // gộp khoảng trắng
            t = t.Replace("\u00A0", " ");      // nbsp
            return t.Trim();
        }
    }
}
