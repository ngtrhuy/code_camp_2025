using TouristApp.Services;

namespace TouristApp
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container
            builder.Services.AddControllers();

            // Swagger/OpenAPI
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // CORS (mở hoàn toàn cho tiện test API)
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            });

            // =======================
            //   CRAWLER REGISTRATIONS
            // =======================
            // Đăng ký HttpClient factory (nếu service muốn inject HttpClient sau này)
            builder.Services.AddHttpClient();

            // Đăng ký service crawl Pystravel bạn đang dùng
            // (KHÔNG còn dùng kiểu PystravelCrawler)
            builder.Services.AddTransient<PystravelCrawlService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseCors("AllowAll");
            app.UseAuthorization();

            app.MapControllers();

            app.Run();
        }
    }
}
