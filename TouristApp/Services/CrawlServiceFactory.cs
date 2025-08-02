using TouristApp.Models;

namespace TouristApp.Services
{
    public static class CrawlServiceFactory
    {
        public static IGenericCrawlService CreateCrawlService(string crawlType)
        {
            return crawlType?.ToLower() switch
            {
                "client_side" => new GenericCrawlServiceClientSide(),
                "server_side" => new GenericCrawlServiceServerSide(),
                _ => new GenericCrawlServiceServerSide() // Default to server-side
            };
        }

        public static IGenericCrawlService CreateCrawlService(PageConfigModel config)
        {
            return CreateCrawlService(config.CrawlType);
        }
    }
} 