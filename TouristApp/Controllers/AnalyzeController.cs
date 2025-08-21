using Microsoft.AspNetCore.Mvc;
using TouristApp.Models;
using TouristApp.Services;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Xml.XPath;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/analyze")]
    public class AnalyzeController : ControllerBase
    {
        private readonly IPageRenderService _renderService;
        private readonly IConfiguration _config;

        public AnalyzeController(IPageRenderService renderService, IConfiguration config)
        {
            _renderService = renderService;
            _config = config;
        }


        private static string PreprocessCss(string css)
        {
            var s = css?.Trim() ?? "";
            if (s.Length == 0) return s;
            // unescape phổ biến do FE tạo
            s = Regex.Replace(s, @"\\([:.\[\]#>+~,])", "$1");
            // bỏ pseudo-elements
            s = Regex.Replace(s, @"::[a-zA-Z\-]+", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        // dùng khi muốn thử XPath nhưng không vỡ 500
        private static HtmlNode? SafeSelectSingle(HtmlNode root, string xpath, out string? error)
        {
            try { error = null; return root.SelectSingleNode(xpath); }
            catch (XPathException ex) { error = ex.Message; return null; }
        }

        private static HtmlNodeCollection SafeSelectNodes(HtmlNode root, string xpath, out string? error)
        {
            try { error = null; return root.SelectNodes(xpath) ?? new HtmlNodeCollection(null); }
            catch (XPathException ex) { error = ex.Message; return new HtmlNodeCollection(null); }
        }

        private static HtmlNode? FindSelectedNode(HtmlDocument doc, SelectionSpec sel)
        {
            if (!string.IsNullOrWhiteSpace(sel.XPath))
                return doc.DocumentNode.SelectSingleNode(sel.XPath);

            if (!string.IsNullOrWhiteSpace(sel.Css))
            {
                var xp = CssToXPath(sel.Css!);
                string? xpErr;
                var node = SafeSelectSingle(doc.DocumentNode, xp, out xpErr);
                if (xpErr != null)
                    throw new InvalidOperationException($"CSS → XPath không hợp lệ. css='{sel.Css}', xpath='{xp}', error='{xpErr}'");
                return node;
            }

            if (!string.IsNullOrWhiteSpace(sel.TextHint))
            {
                var text = sel.TextHint!.Trim();
                var nodes = doc.DocumentNode.SelectNodes($"//*[contains(normalize-space(.), '{EscapeXPathLiteral(text)}')]");
                return nodes?.OrderByDescending(n => (n.InnerText ?? "").Length).FirstOrDefault();
            }
            return null;
        }


        // Chuyển CSS cơ bản => XPath (đủ dùng cho tag, .class, #id, descendant, child, [attr=val])
        private static string CssToXPath(string css)
        {
            // Lấy selector đầu tiên nếu có dấu phẩy
            var first = (css ?? "").Split(',')[0].Trim();
            first = PreprocessCss(first);

            // Tách theo '>' để phân biệt child vs descendant
            var parts = Regex.Split(first, @"\s*>\s*|(?<!>)\s+(?!>)"); // tách theo '>' hoặc khoảng trắng
            var ops = new List<string>();
            var tokens = new List<string>();

            // reconstruct operators: we need to know whether '>' or ' ' was used
            // Simple pass: re-scan original string
            var re = new Regex(@"\s*>\s*|\s+");
            var m = re.Matches(first);
            int idx = 0;
            foreach (Match mm in m)
            {
                ops.Add(mm.Value.Contains(">") ? "/" : "//");
            }

            // Build XPath for each simple selector
            var simpleSelectors = first.Split(new[] { '>' }, StringSplitOptions.RemoveEmptyEntries)
                                       .SelectMany(s => s.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                                       .ToList();

            // But the above could lose order; safer: walk with regex
            var seq = new List<(string selector, string op)>();
            {
                var r = new Regex(@"(?:(\S+))(\s*>\s*|\s+)?");
                var mm = r.Matches(first);
                foreach (Match m2 in mm)
                {
                    var sel = m2.Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(sel)) continue;
                    var op = m2.Groups[2].Success ? (m2.Groups[2].Value.Contains(">") ? "/" : "//") : "//";
                    seq.Add((sel, op));
                }
                if (seq.Count > 0) seq[0] = (seq[0].selector, "//"); // đầu tiên luôn từ document
            }

            string BuildSimple(string s)
            {
                s = PreprocessCss(s);

                // 0) :nth-of-type(n)
                int? nth = null;
                var nthMatch = Regex.Match(s, @":nth-of-type\((\d+)\)", RegexOptions.IgnoreCase);
                if (nthMatch.Success && int.TryParse(nthMatch.Groups[1].Value, out var n)) nth = n;
                s = Regex.Replace(s, @":nth-of-type\([^\)]*\)", "", RegexOptions.IgnoreCase);

                // 0.1) strip các pseudo khác (bao gồm :not(...) – cứ bỏ cho an toàn)
                s = Regex.Replace(s, @":[a-zA-Z\-]+(\([^\)]*\))?", "", RegexOptions.IgnoreCase);

                // 1) [attr=value]
                var attrPreds = new List<string>();
                var attrRe = new Regex(@"\[([^\]=\s]+)=['""]?([^\]'""]+)['""]?\]", RegexOptions.IgnoreCase);
                foreach (Match ma in attrRe.Matches(s))
                {
                    var a = ma.Groups[1].Value.Trim();
                    var v = ma.Groups[2].Value.Trim();
                    attrPreds.Add($"@{a}='{EscapeQuotes(v)}'");
                }
                s = attrRe.Replace(s, "");

                // 2) #id
                var id = "";
                var idRe = new Regex(@"#([A-Za-z0-9_:-]+)");
                foreach (Match mi in idRe.Matches(s)) id = mi.Groups[1].Value;
                s = idRe.Replace(s, "");

                // 3) .class (multiple)
                var classes = new List<string>();
                var clsRe = new Regex(@"\.([A-Za-z0-9_:-]+)"); // cho phép dấu ':', '_' '-'
                foreach (Match mc in clsRe.Matches(s)) classes.Add(mc.Groups[1].Value);
                s = clsRe.Replace(s, "");

                // 4) tag (fallback '*', lọc tag lỗi)
                var tag = string.IsNullOrWhiteSpace(s) ? "*" : s;
                if (!Regex.IsMatch(tag, @"^[A-Za-z][A-Za-z0-9:_-]*$")) tag = "*";

                // 5) predicate
                var preds = new List<string>();
                if (!string.IsNullOrEmpty(id)) preds.Add($"@id='{EscapeQuotes(id)}'");
                foreach (var c in classes)
                    preds.Add($"contains(concat(' ', normalize-space(@class), ' '), ' {EscapeQuotes(c)} ')");
                preds.AddRange(attrPreds);

                var predicate = preds.Count > 0 ? "[" + string.Join(" and ", preds) + "]" : "";
                var simple = $"{tag}{predicate}";

                if (nth.HasValue && nth.Value > 0) simple += $"[{nth.Value}]";
                return simple;
            }
            var xpath = "";
            foreach (var (selector, op) in seq)
            {
                xpath += op + BuildSimple(selector);
            }
            return xpath;
        }

        private static string EscapeQuotes(string s) => s.Replace("'", "&apos;").Replace("\"", "&quot;");
        private static string EscapeXPathLiteral(string s) => s.Replace("'", "&apos;");

        private static (HtmlNode? ancestor, string xpath) AutoDetectItemAncestor(HtmlDocument doc, HtmlNode start)
        {
            // duyệt tối đa 8 cấp cha để tìm node lặp lại hợp lý
            var cur = start;
            var levels = 0;
            HtmlNode? bestNode = null;
            string bestXPath = "";
            int bestCount = 0;

            while (cur != null && cur.Name != "#document" && levels < 8)
            {
                var xp = BuildRepeatingXPath(cur);
                var nodes = doc.DocumentNode.SelectNodes(xp);
                var count = nodes?.Count ?? 0;

                // Ưu tiên khoảng lặp "hợp lý"
                if (count >= 3 && count <= 300)
                {
                    bestNode = cur;
                    bestXPath = xp;
                    bestCount = count;
                    break;
                }

                if (count > bestCount)
                {
                    bestNode = cur;
                    bestXPath = xp;
                    bestCount = count;
                }

                cur = cur.ParentNode;
                levels++;
            }

            return (bestNode, bestXPath);
        }

        private static string BuildRepeatingXPath(HtmlNode node)
        {
            // Tạo XPath bền: //tag[contains(@class,'...') and contains(@class,'...')]
            var tag = node.Name;
            var classes = (node.GetAttributeValue("class", "") ?? "")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(c => c.Length > 2 && !Regex.IsMatch(c, @"^\d") &&
                            !Regex.IsMatch(c, @"^(ng\-|css\-|sc\-|chakra\-|Mui\-)", RegexOptions.IgnoreCase))
                .Distinct()
                .Take(2)
                .ToList();
            var preds = new List<string>();
            foreach (var c in classes)
                preds.Add($"contains(concat(' ', normalize-space(@class), ' '), ' {EscapeQuotes(c)} ')");

            // Nếu không có class, thử id (nhưng id thường unique → không dùng nếu có vẻ động)
            var id = node.GetAttributeValue("id", "");
            if (!string.IsNullOrWhiteSpace(id) && id.Length < 40 && !Regex.IsMatch(id, @"\d{3,}"))
                preds.Add($"@id='{EscapeQuotes(id)}'");

            var predicate = preds.Count > 0 ? "[" + string.Join(" and ", preds) + "]" : "";
            return $"//{tag}{predicate}";
        }

        private static string BuildRelativeXPath(HtmlNode ancestor, HtmlNode node)
        {
            var steps = new List<string>();
            var cur = node;

            while (cur != null && cur != ancestor)
            {
                var tag = cur.Name;
                string step = tag;

                var cls = (cur.GetAttributeValue("class", "") ?? "")
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(c => c.Length > 2 && !Regex.IsMatch(c, @"^\d") &&
                                         !Regex.IsMatch(c, @"^(ng\-|css\-|sc\-|chakra\-|Mui\-)", RegexOptions.IgnoreCase));

                if (!string.IsNullOrWhiteSpace(cls))
                    step += $"[contains(@class,'{EscapeQuotes(cls)}')]";
                else if (cur.ParentNode != null)
                {
                    var same = cur.ParentNode.Elements(tag).ToList();
                    if (same.Count > 1)
                    {
                        var idx = same.IndexOf(cur) + 1;
                        step += $"[{idx}]";
                    }
                }

                steps.Insert(0, step);
                cur = cur.ParentNode;
            }

            // 🔧 Nếu chính nó là ancestor → trả self::* (là node-set hợp lệ)
            if (steps.Count == 0) return ".//self::*";
            return ".//" + string.Join("/", steps);
        }


        private static (string? imageAttr, string? linkAttr) SuggestAttrs(HtmlNode selected)
        {
            string? imgAttr = null, aAttr = null;

            if (string.Equals(selected.Name, "img", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = new[] { "data-src", "src", "data-original", "data-lazy", "srcset" };
                imgAttr = attrs.FirstOrDefault(a => !string.IsNullOrWhiteSpace(selected.GetAttributeValue(a, "")));
            }

            if (string.Equals(selected.Name, "a", StringComparison.OrdinalIgnoreCase))
            {
                var href = selected.GetAttributeValue("href", "");
                if (!string.IsNullOrWhiteSpace(href)) aAttr = "href";
            }

            return (imgAttr, aAttr);
        }

        private static string ExtractValue(HtmlNode? node, string? imageAttr, string? linkAttr)
        {
            if (node == null) return "";

            if (string.Equals(node.Name, "img", StringComparison.OrdinalIgnoreCase))
            {
                var attr = imageAttr ?? "src";
                return node.GetAttributeValue(attr, "");
            }

            if (string.Equals(node.Name, "a", StringComparison.OrdinalIgnoreCase))
            {
                var attr = linkAttr ?? "href";
                var href = node.GetAttributeValue(attr, "");
                return string.IsNullOrWhiteSpace(node.InnerText?.Trim()) ? href : node.InnerText.Trim();
            }

            var text = HtmlEntity.DeEntitize(node.InnerText ?? "").Trim();
            return text;
        }


        // 1) Render HTML để FE preview
        // GET /api/analyze/fetch?url=...&mode=server_side|client_side|auto&loadMoreSelector=&loadMoreClicks=2
        [HttpGet("fetch")]
        public async Task<IActionResult> Fetch([FromQuery] string url, [FromQuery] string mode = "server_side",
                                               [FromQuery] string? loadMoreSelector = null, [FromQuery] int loadMoreClicks = 0)
        {
            if (string.IsNullOrWhiteSpace(url)) return BadRequest("url is required");
            var result = await _renderService.RenderAsync(url, mode, loadMoreSelector, loadMoreClicks);
            return Ok(new
            {
                message = "ok",
                finalUrl = result.FinalUrl,
                baseDomain = result.BaseDomain,
                renderMode = result.RenderModeUsed,
                logs = result.Logs,
                html = result.Html
            });
        }

        // 2) Validate config nháp: chạy crawler thật theo đúng logic hiện có
        // POST /api/analyze/validate
        // body: { "config": { ...PageConfigModel... }, "sampleLimit": 20 }
        [HttpPost("validate")]
        public async Task<IActionResult> Validate([FromBody] ValidateRequest req)
        {
            if (req?.Config == null) return BadRequest("Config is required.");

            try
            {
                var crawlService = CrawlServiceFactory.CreateCrawlService(req.Config);
                // Dùng method mới: CrawlFromConfigAsync
                var data = await crawlService.CrawlFromConfigAsync(req.Config);

                var samples = (req.SampleLimit.HasValue && req.SampleLimit.Value > 0)
                    ? data.Take(req.SampleLimit.Value).ToList()
                    : data.Take(20).ToList();

                var coverage = ComputeCoverage(data);
                var warnings = BuildWarnings(req.Config, data);

                return Ok(new
                {
                    message = $"Validate thành công, lấy được {data.Count} items.",
                    itemsFound = data.Count,
                    perFieldCoverage = coverage,
                    warnings,
                    samples
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Lỗi validate: {ex.Message}" });
            }
        }

        // ---------- Helpers ----------

        private Dictionary<string, double> ComputeCoverage(List<StandardTourModel> data)
        {
            double n = Math.Max(1, data.Count);
            double pct(Func<StandardTourModel, bool> pred) => Math.Round(100.0 * data.Count(pred), 2);

            return new Dictionary<string, double>
            {
                ["TourName"] = pct(t => !string.IsNullOrWhiteSpace(t.TourName)) / n,
                ["TourCode"] = pct(t => !string.IsNullOrWhiteSpace(t.TourCode)) / n,
                ["Price"] = pct(t => !string.IsNullOrWhiteSpace(t.Price)) / n,
                ["ImageUrl"] = pct(t => !string.IsNullOrWhiteSpace(t.ImageUrl)) / n,
                ["DepartureLocation"] = pct(t => !string.IsNullOrWhiteSpace(t.DepartureLocation)) / n,
                ["DepartureDates"] = pct(t => (t.DepartureDates?.Count ?? 0) > 0) / n,
                ["Duration"] = pct(t => !string.IsNullOrWhiteSpace(t.Duration)) / n,
                ["TourDetailUrl"] = pct(t => !string.IsNullOrWhiteSpace(t.TourDetailUrl)) / n,
                ["Schedule(Detail Page)"] = pct(t => (t.Schedule?.Count ?? 0) > 0) / n
            };
        }

        // 3) Tạo selector từ lựa chọn (CSS/XPath/Text) + gợi ý ancestor item + validate nhanh
        // POST /api/analyze/resolve-selection
        [HttpPost("resolve-selection")]
        public async Task<IActionResult> ResolveSelection([FromBody] ResolveSelectionRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.Url))
                return BadRequest("Url is required.");

            // 1) Render HTML trang nguồn
            var render = await _renderService.RenderAsync(
                req.Url, req.Mode, req.LoadMoreSelector, req.LoadMoreClicks
            );
            if (string.IsNullOrEmpty(render.Html))
                return BadRequest("Could not render page HTML.");

            var doc = new HtmlDocument();
            doc.LoadHtml(render.Html);

            // 2) Xác định node được chọn
            HtmlNode? selected = null;
            try { selected = FindSelectedNode(doc, req.Selection); }
            catch (Exception ex) { return BadRequest($"Invalid selector: {ex.Message}"); }

            if (selected == null)
                return NotFound("Không tìm thấy phần tử tương ứng với selection (css/xpath/textHint).");


            // 3) Xác định Item Ancestor
            HtmlNode? itemAncestor = null;
            string itemAncestorXPath = "";

            if (!string.IsNullOrWhiteSpace(req.Ancestor?.XPath))
            {
                itemAncestorXPath = req.Ancestor!.XPath!;
                itemAncestor = doc.DocumentNode.SelectSingleNode(itemAncestorXPath);
            }
            else if (!string.IsNullOrWhiteSpace(req.Ancestor?.Css))
            {
                itemAncestorXPath = CssToXPath(req.Ancestor!.Css!);
                itemAncestor = doc.DocumentNode.SelectSingleNode(itemAncestorXPath);
            }
            else if (req.Ancestor?.Auto == true) // Auto detect
            {
                (itemAncestor, itemAncestorXPath) = AutoDetectItemAncestor(doc, selected);
            }
            else // Manual - selected element is the container itself
            {
                itemAncestor = selected;
                itemAncestorXPath = BuildRepeatingXPath(selected);
            }

            if (itemAncestor == null)
                return BadRequest("Không xác định được Item Ancestor. Hãy truyền ancestor.css/xpath hoặc chọn node khác.");

            // 4) Sinh XPath tương đối từ ancestor -> node field
            var relativeXPath = BuildRelativeXPath(itemAncestor, selected);

            // 5) Gợi ý attr cho IMG/A
            var (imageAttr, linkAttr) = SuggestAttrs(selected);

            // 6) Đánh giá coverage + samples
            var matches = SafeSelectNodes(doc.DocumentNode, itemAncestorXPath, out var ancErr);
            if (ancErr != null)
                return BadRequest($"ItemAncestorXPath không hợp lệ: '{itemAncestorXPath}'. error='{ancErr}'");
            int itemsMatched = matches.Count;
            int withValue = 0;
            var samples = new List<string>();

            foreach (var item in matches.Take(Math.Max(1, req.SampleLimit)))
            {
                var fieldNode = SafeSelectSingle(item, relativeXPath, out var relErr);
                if (relErr != null) break; // relative xpath hỏng → dừng sớm
                var val = ExtractValue(fieldNode, imageAttr, linkAttr);
                if (!string.IsNullOrWhiteSpace(val)) { withValue++; samples.Add(val.Trim()); }
            }

            double coveragePct = itemsMatched == 0 ? 0 : Math.Round(100.0 * withValue / Math.Min(itemsMatched, Math.Max(1, req.SampleLimit)), 2);

            // 7) Cảnh báo/guardrails
            var warnings = new List<string>();
            if (!relativeXPath.StartsWith(".//"))
                warnings.Add("FieldRelativeXPath nên bắt đầu bằng .// (selector tương đối từ item).");
            if (relativeXPath.Contains("/@"))
                warnings.Add("Không dùng /@attr trong XPath field. Hãy trỏ tới element và đặt attr ở ImageAttr/TourDetailAttr.");
            if (itemsMatched <= 1)
                warnings.Add("ItemAncestorXPath có vẻ chưa trỏ đúng thẻ ITEM lặp lại (match ≤ 1).");
            if (!itemAncestorXPath.StartsWith("//"))
                warnings.Add("ItemAncestorXPath nên là XPath tuyệt đối từ document, ví dụ //article[contains(@class,'tour-item')].");

            var resp = new ResolveSelectionResponse
            {
                ItemAncestorXPath = itemAncestorXPath,
                FieldRelativeXPath = relativeXPath,
                AttrSuggestion = new { imageAttr, linkAttr },
                ItemsMatched = itemsMatched,
                FieldCoveragePct = coveragePct,
                Samples = samples,
                Warnings = warnings
            };

            return Ok(resp);
        }


        private List<string> BuildWarnings(PageConfigModel cfg, List<StandardTourModel> data)
        {
            var warns = new List<string>();

            // Ràng buộc chuẩn extractor: field XPath nên là relative ".//"
            void CheckRel(string? label, string? xp)
            {
                if (!string.IsNullOrWhiteSpace(xp) && !xp.TrimStart().StartsWith(".//"))
                    warns.Add($"[{label}] nên dùng XPath tương đối bắt đầu bằng .// theo item node.");
            }

            CheckRel(nameof(cfg.TourName), cfg.TourName);
            CheckRel(nameof(cfg.TourCode), cfg.TourCode);
            CheckRel(nameof(cfg.TourPrice), cfg.TourPrice);
            CheckRel(nameof(cfg.ImageUrl), cfg.ImageUrl);
            CheckRel(nameof(cfg.DepartureLocation), cfg.DepartureLocation);
            CheckRel(nameof(cfg.DepartureDate), cfg.DepartureDate);
            CheckRel(nameof(cfg.TourDuration), cfg.TourDuration);
            CheckRel(nameof(cfg.TourDetailUrl), cfg.TourDetailUrl);

            // Cảnh báo attr
            if (!string.IsNullOrWhiteSpace(cfg.ImageUrl) && cfg.ImageUrl.Contains("/@"))
                warns.Add("[ImageUrl] XPath không nên kết thúc bằng /@attr. Hãy trỏ tới <img> và đặt ImageAttr = src/data-src.");
            if (!string.IsNullOrWhiteSpace(cfg.TourDetailUrl) && cfg.TourDetailUrl.Contains("/@"))
                warns.Add("[TourDetailUrl] XPath không nên kết thúc bằng /@href. Hãy trỏ tới <a> và đặt TourDetailAttr = href.");

            // Nếu không lấy được item nào
            if (data.Count == 0) warns.Add("Không crawl được item nào. Kiểm tra lại TourListSelector/PagingType.");

            return warns.Distinct().ToList();
        }
    }

    // DTOs
    public class ValidateRequest
    {
        public PageConfigModel? Config { get; set; }
        public int? SampleLimit { get; set; }
    }
}
