using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using TouristApp.Services;
using TouristApp.Services.LuaViet;
using TouristApp.Data;

var builder = WebApplication.CreateBuilder(args);

// 1. Đăng ký DbContext với MySQL
builder.Services.AddDbContext<TourDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("MySql"),
        new MySqlServerVersion(new Version(8, 0, 30))
    )
);

// 2. Đăng ký các service
builder.Services.AddScoped<TourScraperService>();
builder.Services.AddScoped<TourSeleniumService>();
builder.Services.AddScoped<TourImportService>();
builder.Services.AddHttpClient<PystravelCrawlService>();
builder.Services.AddHttpClient<LuaVietTourCrawler>();

// 3. Controller & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 4. CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

// 5. Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Áp dụng migration DB (tùy chọn, chỉ trong dev)
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TourDbContext>();
        dbContext.Database.Migrate();
    }
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

app.Run();
