# Hướng Dẫn Sử Dụng Page Analyzer Tool

## Tổng Quan

Page Analyzer Tool là một công cụ trực quan giúp phân tích các trang web du lịch và tự động tạo cấu hình crawling thông qua việc inspect element. Công cụ này cho phép bạn dễ dàng tạo XPath selectors mà không cần hiểu biết sâu về HTML hoặc XPath.

## Cấu Trúc Hệ Thống

### Frontend (React.js)
- **File chính**: `AnalyzePage.js`
- **CSS**: `analyze.css`
- **APIs**: `analyzeApi.js`

### Backend (ASP.NET Core)
- **Controller**: `AnalyzeController.cs`
- **Service**: `PageRenderService.cs`
- **Models**: `AnalyzeDTO.cs`, `PageConfigModel.cs`

---

## Workflow Hoàn Chỉnh

### Bước 1: Fetch Preview
1. **Nhập URL** của trang web cần phân tích
2. **Chọn render mode**:
   - `server_side`: Sử dụng HttpClient (nhanh, phù hợp với trang tĩnh)
   - `client_side`: Sử dụng Selenium (chậm hơn, phù hợp với trang động)
   - `auto`: Tự động detect (thử server_side trước, fallback về client_side)
3. **Cấu hình load more** (nếu cần):
   - Load more selector: CSS selector hoặc XPath của nút "Load more"
   - Load more clicks: Số lần click nút để load thêm data
4. **Click "Fetch"** để tải preview

### Bước 2: Inspect & Map Fields

#### 2.1. Main Page Fields
**Các field cần map cho trang danh sách tour:**

| Field | Mô tả | XPath Type | Ví dụ |
|-------|--------|------------|-------|
| `TourListSelector` | Container của từng tour item | Absolute | `//div[contains(@class,'tour-item')]` |
| `TourName` | Tiêu đề tour | Relative | `.//h3[contains(@class,'title')]` |
| `TourPrice` | Giá tour | Relative | `.//div[contains(@class,'price')]` |
| `TourCode` | Mã tour | Relative | `.//span[contains(@class,'code')]` |
| `ImageUrl` | Hình ảnh tour | Relative | `.//img[contains(@class,'thumbnail')]` |
| `TourDetailUrl` | Link chi tiết tour | Relative | `.//a[contains(@class,'detail-link')]` |
| `DepartureLocation` | Điểm khởi hành | Relative | `.//span[contains(@class,'departure')]` |
| `DepartureDate` | Ngày khởi hành | Relative | `.//div[contains(@class,'date')]` |
| `TourDuration` | Thời gian tour | Relative | `.//span[contains(@class,'duration')]` |

#### 2.2. Cách Inspect
1. **Click nút 🎯** bên cạnh field cần map
2. **Click vào element** tương ứng trên preview page
3. **XPath tự động** được tạo và điền vào input field
4. **Kiểm tra samples** để đảm bảo XPath đúng

#### 2.3. Lưu Ý Quan Trọng
- **TourListSelector phải map trước**: Đây là container chính
- **Các field khác**: XPath tương đối từ TourListSelector
- **Kiểm tra ItemsMatched**: Số lượng tour items được match
- **Kiểm tra FieldCoverage**: Tỷ lệ % field có data

### Bước 3: Chuyển Sang Detail Page

#### 3.1. Cách Chuyển Trang
1. **Tắt inspect mode** (nếu đang bật)
2. **Click vào tour link** bất kỳ trong preview
3. **Trang detail tự động load** và chuyển sang Detail Page Mode

#### 3.2. Detail Page Fields
**Các field cần map cho trang chi tiết tour:**

| Field | Mô tả | XPath Type | Ví dụ |
|-------|--------|------------|-------|
| `TourDetailDayTitle` | Tiêu đề từng ngày trong lịch trình | Absolute | `//h4[contains(@class,'day-title')]` |
| `TourDetailDayContent` | Nội dung từng ngày | Absolute | `//div[contains(@class,'day-content')]` |
| `TourDetailNote` | Ghi chú tour | Absolute | `//div[contains(@class,'note')]` |

#### 3.3. Quay Lại Main Page
- **Click "← Back to Main Page"**
- **Config main page được restore** đầy đủ
- **Trang main tự động reload**

### Bước 4: Validate Configuration
1. **Click "Validate config"** để test thử config
2. **Kiểm tra kết quả**:
   - Items Found: Số tour được crawl
   - Coverage: Tỷ lệ % data cho từng field
   - Warnings: Cảnh báo về config
   - Samples: Data mẫu được crawl
3. **Sửa config** nếu cần thiết

---

## Các Tính Năng Nâng Cao

### Auto XPath Generation
- **CSS → XPath conversion**: Tự động convert CSS selector thành XPath
- **Smart class filtering**: Bỏ qua các class động (ng-, css-, sc-, chakra-, Mui-)
- **Relative/Absolute detection**: Tự động chọn prefix phù hợp

### Smart Element Detection
- **Auto ancestor detection**: Tự động tìm container cho tour items
- **Attribute suggestion**: Gợi ý attr cho image (src, data-src) và link (href)
- **Coverage analysis**: Phân tích độ chính xác của XPath

### Page Rendering
- **Multiple render modes**: Server-side, client-side, auto
- **Dynamic content support**: Load more, carousel, infinite scroll
- **URL normalization**: Tự động chuẩn hóa URL format

---

## Cấu Hình Output

### PageConfigModel Structure
```csharp
public class PageConfigModel
{
    // Basic Info
    public string BaseDomain { get; set; }
    public string BaseUrl { get; set; }
    public string CrawlType { get; set; }
    public string PagingType { get; set; }
    
    // Main Page Selectors
    public string TourListSelector { get; set; }
    public string TourName { get; set; }
    public string TourPrice { get; set; }
    public string TourCode { get; set; }
    public string ImageUrl { get; set; }
    public string TourDetailUrl { get; set; }
    public string DepartureLocation { get; set; }
    public string DepartureDate { get; set; }
    public string TourDuration { get; set; }
    
    // Attributes
    public string ImageAttr { get; set; } = "src";
    public string TourDetailAttr { get; set; } = "href";
    
    // Paging Config
    public string LoadMoreButtonSelector { get; set; }
    public string LoadMoreType { get; set; }
    
    // Detail Page Selectors
    public string TourDetailDayTitle { get; set; }
    public string TourDetailDayContent { get; set; }
    public string TourDetailNote { get; set; }
}
```

### XPath Patterns
- **Absolute XPath**: `//tag[contains(@class,'classname')]`
- **Relative XPath**: `.//tag[contains(@class,'classname')]`
- **Multiple conditions**: `//tag[contains(@class,'class1') and contains(@class,'class2')]`

---

## Troubleshooting

### Các Lỗi Thường Gặp

#### 1. Không Tải Được Preview
- **Nguyên nhân**: URL không hợp lệ, CORS policy, timeout
- **Giải pháp**: 
  - Kiểm tra URL format
  - Thử đổi render mode
  - Kiểm tra network connectivity

#### 2. XPath Không Match
- **Nguyên nhân**: Element không tồn tại, XPath sai syntax
- **Giải pháp**:
  - Re-inspect element
  - Kiểm tra class names
  - Thử XPath đơn giản hơn

#### 3. Coverage Thấp
- **Nguyên nhân**: XPath quá specific, structure không đồng nhất
- **Giải pháp**:
  - Simplify XPath
  - Inspect element khác
  - Kiểm tra HTML structure

#### 4. Detail Page Không Load
- **Nguyên nhân**: Link relative, CORS, redirect
- **Giải pháp**:
  - Kiểm tra tour detail URL format
  - Đảm bảo base domain đúng
  - Thử manual navigation

---

## Best Practices

### XPath Design
1. **Ưu tiên class selector** thay vì structure-based
2. **Tránh index-based selector** (nth-child)
3. **Sử dụng contains()** thay vì exact match
4. **Kiểm tra multiple samples** trước khi finalize

### Testing Strategy
1. **Test với multiple pages** của cùng website
2. **Kiểm tra edge cases** (tour hết slot, giá khuyến mãi)
3. **Validate performance** (speed, accuracy)
4. **Monitor changes** theo thời gian

### Configuration Management
1. **Document XPath logic** và reasoning
2. **Version control** cho configs
3. **Backup configs** trước khi modify
4. **Test regression** sau mỗi update

---

## API Documentation

### Endpoints

#### 1. Fetch Preview
```http
GET /api/analyze/fetch?url={url}&mode={mode}&loadMoreSelector={selector}&loadMoreClicks={clicks}
```

#### 2. Resolve Selection
```http
POST /api/analyze/resolve-selection
Content-Type: application/json

{
  "url": "https://example.com",
  "mode": "client_side",
  "selection": { "css": "div.tour-item" },
  "ancestor": { "auto": true },
  "sampleLimit": 20
}
```

#### 3. Validate Config
```http
POST /api/analyze/validate
Content-Type: application/json

{
  "config": { /* PageConfigModel */ },
  "sampleLimit": 20
}
```

### Response Formats

#### Resolve Selection Response
```json
{
  "itemAncestorXPath": "//div[contains(@class,'tour-item')]",
  "fieldRelativeXPath": ".//h3[contains(@class,'title')]",
  "attrSuggestion": {
    "imageAttr": "data-src",
    "linkAttr": "href"
  },
  "itemsMatched": 24,
  "fieldCoveragePct": 95.8,
  "samples": ["Sample 1", "Sample 2"],
  "warnings": ["Warning message"]
}
```

---

## Kết Luận

Page Analyzer Tool cung cấp một workflow hoàn chỉnh để phân tích và tạo cấu hình crawling cho các trang web du lịch. Với giao diện trực quan và tính năng auto-generation, tool này giúp giảm thiểu thời gian và công sức cần thiết để setup crawling system.

Để sử dụng hiệu quả, hãy:
1. **Hiểu rõ structure** của trang web cần crawl
2. **Tuân thủ workflow** từ main page → detail page
3. **Kiểm tra kỹ samples** và coverage
4. **Test thoroughly** trước khi production

Tool này là nền tảng để xây dựng hệ thống crawling mạnh mẽ và linh hoạt cho các trang web du lịch.
