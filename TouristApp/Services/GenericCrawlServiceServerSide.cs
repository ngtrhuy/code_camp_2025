using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
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

            var normalizedBaseDomain = NormalizeBaseDomain(config.BaseDomain); // https://domain.tld
            var hostOnly = GetHost(normalizedBaseDomain);                      // domain.tld

            foreach (var baseUrl in baseUrls)
            {
                int currentPage = 1;
                bool hasMore = true;

                while (hasMore)
                {
                    string pageUrl = baseUrl;
                    if (config.PagingType == "querystring")
                        pageUrl += pageUrl.Contains("?") ? $"&page={currentPage}" : $"?page={currentPage}";

                    var html = await client.GetStringAsync(pageUrl);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var nodes = doc.DocumentNode.SelectNodes(config.TourListSelector);
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

                                // ✅ KHÔNG tạo Uri trực tiếp nữa
                                SourceSite = hostOnly
                            };

                            if (string.IsNullOrWhiteSpace(tour.TourCode))
                                tour.TourCode = MakeSafeCode(tour.TourName, tour.TourDetailUrl);
                            tour.Duration = TrimDuration(tour.Duration);

                            tours.Add(tour);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Parse tour lỗi: {ex.Message}");
                        }
                    }

                    currentPage++;
                    if (config.PagingType != "querystring") break;
                }
            }

            foreach (var tour in tours)
            {
                if (!string.IsNullOrEmpty(tour.TourDetailUrl))
                {
                    var fullUrl = BuildAbsoluteUrl(normalizedBaseDomain, tour.TourDetailUrl);
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

            var displayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "dich vu bao gom", "DỊCH VỤ BAO GỒM" },
                { "dich vu khong bao gom", "DỊCH VỤ KHÔNG BAO GỒM" },
                { "chi phi tre em", "CHI PHÍ TRẺ EM" },
                { "ky hop dong & dat coc tour", "KÝ HỢP ĐỒNG & ĐẶT CỌC TOUR" },
                { "quy dinh huy tour", "QUY ĐỊNH HỦY TOUR" },
            };

            static string CanonizeHeading(string? raw)
            {
                var x = ToAsciiLower(CleanText(raw));
                if (Regex.IsMatch(x, @"\b(khong|chua)\s*bao\s*gom\b|\bnot\s*include(?:d)?\b|\bexclude(?:d)?\b")) return "dich vu khong bao gom";
                if (Regex.IsMatch(x, @"\b(bao\s*gom|gia\s*bao\s*gom|dich\s*vu\s*bao\s*gom)\b|(?<!not\s)include(?:d)?\b")) return "dich vu bao gom";
                if (Regex.IsMatch(x, @"\b(chi\s*phi\s*tre\s*em|chinh\s*sach\s*tre\s*em|tre\s*em|em\s*be)\b")) return "chi phi tre em";
                if (Regex.IsMatch(x, @"\b(ky|ki)\s*hop\s*dong\b|\bdat\s*coc\b|\bdat\s*coc\s*tour\b|\bthanh\s*toan\b|\bh[oô] sơ.*visa\b|\bvisa.*h[oô] sơ\b|\bl[ịi]ch\s*hẹn\b")) return "ky hop dong & dat coc tour";
                if (Regex.IsMatch(x, @"\b(quy\s*din[h]?h\s*h[u]y\s*tour|dieu\s*kien\s*h[u]y|chinh\s*sach\s*h[u]y|h[u]y\s*tour|phi\s*h[u]y)\b")) return "quy dinh huy tour";
                return string.Empty;
            }

            var bucket = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            void Merge(string canon, IEnumerable<string> items)
            {
                if (string.IsNullOrWhiteSpace(canon) || !displayMap.ContainsKey(canon)) return;
                if (!bucket.TryGetValue(canon, out var set))
                    bucket[canon] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var it in items)
                {
                    var t = CleanText(it);
                    if (!string.IsNullOrWhiteSpace(t)) set.Add(t);
                }
            }

            HtmlNode scope = doc.DocumentNode;
            if (!string.IsNullOrWhiteSpace(config.TourDetailNote))
            {
                var byCfg = doc.DocumentNode.SelectSingleNode(config.TourDetailNote);
                if (byCfg != null) scope = byCfg;
            }
            else
            {
                var candidates = doc.DocumentNode.SelectNodes("//div|//section") ?? new HtmlNodeCollection(null);
                int Score(HtmlNode n)
                {
                    var heads = n.SelectNodes(".//*[self::h1 or self::h2 or self::h3 or self::h4 or self::h5 or self::h6 or self::strong or self::b or (self::p and (./strong or ./b))]");
                    if (heads == null) return 0;
                    int ok = 0;
                    foreach (var h in heads)
                    {
                        var text = h.Name.Equals("p", StringComparison.OrdinalIgnoreCase)
                            ? h.SelectSingleNode("./strong|./b")?.InnerText ?? h.InnerText
                            : h.InnerText;
                        if (!string.IsNullOrWhiteSpace(CanonizeHeading(text))) ok++;
                    }
                    return ok;
                }
                var best = candidates.OrderByDescending(Score).FirstOrDefault(n => Score(n) >= 2);
                if (best != null) scope = best;
            }

            ExtractByDocumentOrder(scope, CanonizeHeading, Merge);
            ReclassifyMisplaced(bucket);

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in displayMap)
                result[kv.Value] = bucket.TryGetValue(kv.Key, out var items) && items.Count > 0 ? string.Join("\n", items) : string.Empty;
            tour.ImportantNotes = result;

            if (tour.DepartureDates == null || tour.DepartureDates.Count == 0)
            {
                var dateNodes = doc.DocumentNode.SelectNodes("//ul[contains(@class, 'tdetail-date')]/li");
                if (dateNodes != null)
                {
                    tour.DepartureDates = dateNodes
                        .Select(li => li.InnerText.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => Regex.Match(s, @"\d{1,2}/\d{1,2}").Value)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct()
                        .ToList();
                }
            }
        }

        // ================== Helpers: URL/DOMAIN ==================
        private static string NormalizeBaseDomain(string baseDomain)
        {
            var s = (baseDomain ?? "").Trim();
            if (string.IsNullOrEmpty(s)) return "";
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                s = "https://" + s;
            // bỏ dấu '/' cuối
            s = s.TrimEnd('/');
            return s;
        }

        private static string GetHost(string normalizedBaseDomain)
        {
            if (Uri.TryCreate(normalizedBaseDomain, UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.Host))
                return u.Host.ToLowerInvariant();
            // fallback tách tay
            return normalizedBaseDomain
                .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
                .Replace("https://", "", StringComparison.OrdinalIgnoreCase)
                .Trim('/')
                .ToLowerInvariant();
        }

        private static string BuildAbsoluteUrl(string normalizedBaseDomain, string maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return normalizedBaseDomain;
            if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out var abs)) return abs.ToString();
            // relative
            var root = normalizedBaseDomain.TrimEnd('/');
            var rel = maybeRelative.TrimStart('/');
            return $"{root}/{rel}";
        }

        // ======== Anchor regex ========
        private static readonly Regex AnchorRegex = new Regex(
            @"(?:(?<=^)|(?<=[\s\(\[\{""'“”‘’\-–—,;:]))(?<inc>\b(DỊCH\s*VỤ\s*BAO\s*GỒM|GIÁ\s*BAO\s*GỒM|INCLUDED?|INCLUDE)\b)(?=\s*[:;\-–—\.\""“”'’»)\]]|\s*$)"
          + @"|(?:(?<=^)|(?<=[\s\(\[\{""'“”‘’\-–—,;:]))(?<exc>\b(DỊCH\s*VỤ\s*KHÔNG\s*BAO\s*GỒM|GIÁ\s*KHÔNG\s*BAO\s*GỒM|KHÔNG\s*BAO\s*GỒM|CHƯA\s*BAO\s*GỒM|NOT\s*INCLUDED?|EXCLUDED?)\b)(?=\s*[:;\-–—\.\""“”'’»)\]]|\s*$)"
          + @"|(?:(?<=^)|(?<=[\s\(\[\{""'“”‘’\-–—,;:]))(?<child>\b(CHI\s*PHÍ\s*TRẺ\s*EM|CHÍNH\s*SÁCH\s*TRẺ\s*EM|TRẺ\s*EM)\b)(?=\s*[:;\-–—\.\""“”'’»)\]]|\s*$)"
          + @"|(?:(?<=^)|(?<=[\s\(\[\{""'“”‘’\-–—,;:]))(?<contract>\b((KÝ|KÍ)\s*HỢP\s*ĐỒNG|ĐẶT\s*CỌC|CỌC\s*TIỀN|THANH\s*TOÁN|HỒ\s*SƠ.*VISA|VISA.*HỒ\s*SƠ|LỊCH\s*HẸN.*(ĐẠI\s*SỨ|LÃNH\s*SỨ))\b)(?=\s*[:;\-–—\.\""“”'’»)\]]|\s*$)"
          + @"|(?:(?<=^)|(?<=[\s\(\[\{""'“”‘’\-–—,;:]))(?<cancel>\b(QUY\s*ĐỊNH\s*HỦY\s*TOUR|ĐIỀU\s*KIỆN\s*HỦY|CHÍNH\s*SÁCH\s*HỦY|HỦY\s*TOUR|PHÍ\s*HỦY)\b)(?=\s*[:;\-–—\.\""“”'’»)\]]|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static string CanonFromMatch(Match m)
        {
            if (m.Groups["exc"].Success) return "dich vu khong bao gom";
            if (m.Groups["inc"].Success) return "dich vu bao gom";
            if (m.Groups["child"].Success) return "chi phi tre em";
            if (m.Groups["contract"].Success) return "ky hop dong & dat coc tour";
            if (m.Groups["cancel"].Success) return "quy dinh huy tour";
            return string.Empty;
        }

        private static void ExtractByDocumentOrder(
            HtmlNode scope,
            Func<string?, string> canonizeHeading,
            Action<string, IEnumerable<string>> merge)
        {
            var all = scope.DescendantsAndSelf().ToList();
            var idx = new Dictionary<HtmlNode, int>();
            for (int i = 0; i < all.Count; i++) idx[all[i]] = i;

            bool IsHeading(HtmlNode n)
            {
                if (n.NodeType != HtmlNodeType.Element) return false;
                var tag = n.Name.ToLowerInvariant();
                if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "strong" or "b") return true;
                if (tag == "p")
                {
                    var strong = n.SelectSingleNode("./strong|./b");
                    if (strong != null && CleanText(n.InnerText) == CleanText(strong.InnerText)) return true;
                }
                return false;
            }

            var heads = scope.Descendants()
                .Where(IsHeading)
                .Select(h =>
                {
                    var text = h.Name == "p"
                        ? (h.SelectSingleNode("./strong|./b")?.InnerText ?? h.InnerText)
                        : h.InnerText;
                    return new { Node = h, Canon = canonizeHeading(text) };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Canon))
                .OrderBy(x => idx[x.Node])
                .ToList();

            if (heads.Count == 0) return;

            for (int i = 0; i < heads.Count; i++)
            {
                var start = idx[heads[i].Node];
                var end = (i + 1 < heads.Count) ? idx[heads[i + 1].Node] : int.MaxValue;
                var currentCanon = heads[i].Canon;

                IEnumerable<HtmlNode> Lists(string tag) =>
                    scope.Descendants(tag).Where(n => idx.TryGetValue(n, out var ni) && ni > start && ni < end);

                var lists = Lists("ul").Concat(Lists("ol")).ToList();
                var hadAny = false;

                if (lists.Count > 0)
                {
                    hadAny = true;
                    foreach (var l in lists)
                    {
                        var lis = l.SelectNodes("./li");
                        if (lis == null || lis.Count == 0)
                        {
                            ProcessTextChunk(CleanText(l.InnerText), currentCanon, CanonFromAnchorOrHeading, merge);
                            continue;
                        }
                        foreach (var li in lis)
                            ProcessTextChunk(CleanText(li.InnerText), currentCanon, CanonFromAnchorOrHeading, merge);
                    }
                }

                if (!hadAny)
                {
                    var blocks = scope.Descendants()
                        .Where(n => (n.Name is "p" or "div" or "section") && idx[n] > start && idx[n] < end)
                        .ToList();

                    foreach (var b in blocks)
                        ProcessTextChunk(CleanText(b.InnerText), currentCanon, CanonFromAnchorOrHeading, merge);
                }
            }
        }

        private static void ProcessTextChunk(
            string text,
            string defaultCanon,
            Func<string, string> canonize,
            Action<string, IEnumerable<string>> merge)
        {
            var raw = (text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(raw)) return;

            var lines = HtmlEntity.DeEntitize(raw)
                .Replace("\r", "\n")
                .Split('\n')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s));

            string currentCanon = defaultCanon;

            foreach (var line in lines)
            {
                var s = line;

                // Dòng chỉ là anchor
                var onlyAnchor = AnchorRegex.Match(s);
                if (onlyAnchor.Success && onlyAnchor.Index == 0 && onlyAnchor.Length == s.Length)
                {
                    currentCanon = CanonFromMatch(onlyAnchor);
                    continue;
                }

                // Nhiều anchor trong một dòng
                var matches = AnchorRegex.Matches(s);
                if (matches.Count == 0)
                {
                    merge(currentCanon, new[] { s });
                    continue;
                }

                int cursor = 0;
                for (int i = 0; i < matches.Count; i++)
                {
                    var m = matches[i];

                    if (m.Index > cursor)
                    {
                        var left = s.Substring(cursor, m.Index - cursor).Trim();
                        if (!string.IsNullOrWhiteSpace(left))
                            merge(currentCanon, new[] { left });
                    }

                    currentCanon = CanonFromMatch(m);

                    int startContent = m.Index + m.Length;
                    var after = s.Substring(startContent);
                    after = Regex.Replace(after, @"^\s*[:;,\-–—\.\""“”'’»)\]]\s*", "");

                    if (i + 1 < matches.Count)
                    {
                        var next = matches[i + 1];
                        var seg = after.Substring(0, Math.Max(0, next.Index - startContent)).Trim();
                        if (!string.IsNullOrWhiteSpace(seg))
                            merge(currentCanon, new[] { seg });
                        cursor = next.Index;
                    }
                    else
                    {
                        var tail = after.Trim();
                        if (!string.IsNullOrWhiteSpace(tail))
                            merge(currentCanon, new[] { tail });
                        cursor = s.Length;
                    }
                }

                if (cursor < s.Length)
                {
                    var rest = s.Substring(cursor).Trim();
                    if (!string.IsNullOrWhiteSpace(rest))
                        merge(currentCanon, new[] { rest });
                }
            }
        }

        private static string CanonFromAnchorOrHeading(string text)
        {
            var m = AnchorRegex.Match(text);
            if (m.Success) return CanonFromMatch(m);

            var x = ToAsciiLower(CleanText(text));
            if (Regex.IsMatch(x, @"\b(khong|chua)\s*bao\s*gom\b|\bnot\s*include(?:d)?\b|\bexclude(?:d)?\b")) return "dich vu khong bao gom";
            if (Regex.IsMatch(x, @"\b(bao\s*gom|gia\s*bao\s*gom|dich\s*vu\s*bao\s*gom)\b|(?<!not\s)include(?:d)?\b")) return "dich vu bao gom";
            if (Regex.IsMatch(x, @"\b(chi\s*phi\s*tre\s*em|chinh\s*sach\s*tre\s*em|tre\s*em|em\s*be)\b")) return "chi phi tre em";
            if (Regex.IsMatch(x, @"\b(ky|ki)\s*hop\s*dong\b|\bdat\s*coc\b|\bdat\s*coc\s*tour\b|\bthanh\s*toan\b|\bh[oô] sơ.*visa\b|\bvisa.*h[oô] sơ\b|\bl[ịi]ch\s*hẹn\b")) return "ky hop dong & dat coc tour";
            if (Regex.IsMatch(x, @"\b(quy\s*din[h]?h\s*h[u]y\s*tour|dieu\s*kien\s*h[u]y|chinh\s*sach\s*h[u]y|h[u]y\s*tour|phi\s*h[u]y)\b")) return "quy dinh huy tour";
            return string.Empty;
        }

        private static void ReclassifyMisplaced(Dictionary<string, HashSet<string>> bucket)
        {
            if (bucket.TryGetValue("chi phi tre em", out var childSet))
            {
                var original = childSet.ToList();
                foreach (var line in original)
                {
                    if (AnchorRegex.IsMatch(line) && !Regex.IsMatch(ToAsciiLower(line), @"\btre\s*em\b"))
                    {
                        childSet.Remove(line);
                        ProcessTextChunk(
                            line,
                            "chi phi tre em",
                            CanonFromAnchorOrHeading,
                            (canon, items) =>
                            {
                                if (!bucket.TryGetValue(canon, out var set))
                                    bucket[canon] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var it in items)
                                {
                                    var t = CleanText(it);
                                    if (!string.IsNullOrWhiteSpace(t)) set.Add(t);
                                }
                            });
                    }
                }
            }

            string ClassifyLine(string line)
            {
                var x = ToAsciiLower(CleanText(line));
                if (Regex.IsMatch(x, @"\b(quy\s*din[h]?h\s*h[u]y\s*tour|dieu\s*kien\s*h[u]y|chinh\s*sach\s*h[u]y|h[u]y\s*tour|phi\s*h[u]y)\b"))
                    return "quy dinh huy tour";
                if (Regex.IsMatch(x, @"\b(ky|ki)\s*hop\s*dong\b|\bdat\s*coc\b|\bdat\s*coc\s*tour\b|\bthanh\s*toan\b|\bh[oô] sơ.*visa\b|\bvisa.*h[oô] sơ\b|\bl[ịi]ch\s*hẹn\b"))
                    return "ky hop dong & dat coc tour";
                return string.Empty;
            }

            var moves = new List<(string from, string to, string line)>();
            foreach (var kv in bucket.ToList())
            {
                foreach (var line in kv.Value)
                {
                    var dest = ClassifyLine(line);
                    if (!string.IsNullOrWhiteSpace(dest) &&
                        !dest.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        moves.Add((kv.Key, dest, line));
                    }
                }
            }
            foreach (var mv in moves)
            {
                if (!bucket.TryGetValue(mv.to, out var tset))
                    bucket[mv.to] = tset = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bucket[mv.from].Remove(mv.line);
                tset.Add(mv.line);
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
        // ================== Helpers khác ==================
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
            t = Regex.Replace(t, @"\s+", " ");
            t = t.Replace("\u00A0", " ");
            return t.Trim();
        }

        private static string ToAscii(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = HtmlEntity.DeEntitize(s);
            var norm = t.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in norm)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string MakeSafeCode(string? name, string? url)
        {
            string raw = "";
            if (!string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var u))
                        raw = u.Segments.Last().Trim('/');
                    else
                        raw = url.Split('/').Last();
                }
                catch { }
            }
            if (string.IsNullOrWhiteSpace(raw)) raw = name ?? "";

            raw = ToAscii(raw).ToUpperInvariant();
            raw = Regex.Replace(raw, @"[^A-Z0-9]+", "");
            if (string.IsNullOrWhiteSpace(raw)) raw = "TOUR" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            return raw.Length > 80 ? raw[..80] : raw;
        }

        private static string TrimDuration(string? s)
        {
            s = (s ?? "").Trim();
            return s.Length > 250 ? s[..250] : s;
        }
    }
}
