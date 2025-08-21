using TouristApp.Models;

namespace TouristApp.Services
{
    public interface IGenericCrawlService
    {
        Task<List<StandardTourModel>> CrawlFromPageConfigAsync(int configId);
        Task<PageConfigModel?> LoadPageConfig(int id);

        Task<List<StandardTourModel>> CrawlFromConfigAsync(PageConfigModel config);
    }
} 