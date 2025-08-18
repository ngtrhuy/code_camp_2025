using MySqlConnector;
using TouristApp.Models;

namespace TouristApp.Services
{
    public interface IHistoryRepository
    {
        Task<int> CreateAsync(int configId, string status, string log = "");
        Task UpdateAsync(int id, string status, string log = "");
        Task<HistoryModel?> GetAsync(int id);
        Task<List<HistoryModel>> GetAllAsync();
    }

    public class HistoryRepository : IHistoryRepository
    {
        private readonly string _connStr;
        public HistoryRepository(IConfiguration cfg)
        {
            _connStr = cfg.GetConnectionString("DefaultConnection")!;
        }

        public async Task<int> CreateAsync(int configId, string status, string log = "")
        {
            await using var conn = new MySqlConnection(_connStr);
            await conn.OpenAsync();
            var cmd = new MySqlCommand(@"
                INSERT INTO history (config_id, crawl_date, status, log)
                VALUES (@ConfigId, NOW(), @Status, @Log);
                SELECT LAST_INSERT_ID();", conn);
            cmd.Parameters.AddWithValue("@ConfigId", configId);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Log", log ?? "");
            var id = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            return id;
        }

        public async Task UpdateAsync(int id, string status, string log = "")
        {
            await using var conn = new MySqlConnection(_connStr);
            await conn.OpenAsync();
            var cmd = new MySqlCommand(
                @"UPDATE history SET status=@Status, log=@Log WHERE id=@Id;", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@Log", log ?? "");
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<HistoryModel?> GetAsync(int id)
        {
            await using var conn = new MySqlConnection(_connStr);
            await conn.OpenAsync();
            var cmd = new MySqlCommand(
                @"SELECT id, config_id, crawl_date, status, log FROM history WHERE id=@Id", conn);
            cmd.Parameters.AddWithValue("@Id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            return new HistoryModel
            {
                Id = r.GetInt32("id"),
                ConfigId = r.GetInt32("config_id"),
                CrawlDate = r.GetDateTime("crawl_date"),
                Status = r.GetString("status"),
                Log = r.IsDBNull(r.GetOrdinal("log")) ? "" : r.GetString("log")
            };
        }

        public async Task<List<HistoryModel>> GetAllAsync()
        {
            var list = new List<HistoryModel>();
            await using var conn = new MySqlConnection(_connStr);
            await conn.OpenAsync();
            var cmd = new MySqlCommand(
                @"SELECT id, config_id, crawl_date, status, log FROM history ORDER BY id DESC", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new HistoryModel
                {
                    Id = r.GetInt32("id"),
                    ConfigId = r.GetInt32("config_id"),
                    CrawlDate = r.GetDateTime("crawl_date"),
                    Status = r.GetString("status"),
                    Log = r.IsDBNull(r.GetOrdinal("log")) ? "" : r.GetString("log")
                });
            }
            return list;
        }
    }
}
