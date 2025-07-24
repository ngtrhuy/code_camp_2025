using Microsoft.AspNetCore.Mvc;
using TouristApp.Models;
using TouristApp.Services;

namespace TouristApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeVietTourController : ControllerBase
    {
        private readonly DeVietTourCrawler _crawler;

        public DeVietTourController()
        {
            _crawler = new DeVietTourCrawler();
        }

        [HttpGet("crawl")]
        public async Task<IActionResult> CrawlTours()
        {
            var urls = new List<string>
            {
                "https://deviet.vn/du-lich/tour-du-lich-chau-au-tron-goi/",
                "https://deviet.vn/gioi-thieu-tour-ghep-du-lich-chau-au/",
                "https://deviet.vn/du-lich/tour-nuoc-ngoai/"
            };

            var allTours = new List<DeVietTourInfo>();

            foreach (var url in urls)
            {
                var tours = await _crawler.CrawlToursAsync(url);
                allTours.AddRange(tours);
            }

            return Ok(allTours);
        }
    }
}
