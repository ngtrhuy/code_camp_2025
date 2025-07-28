using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using TouristApp.Services;
using TouristApp.Data; // Thêm namespace cho DbContext

var builder = WebApplication.CreateBuilder(args);

// 1. Đăng ký DbContext với MySQL
builder.Services.AddDbContext<TourDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("MySql"),
        new MySqlServerVersion(new Version(8, 0, 30)) // Phiên bản MySQL server
    )
);

// 2. Đăng ký các dịch vụ (Service + Controller)
builder.Services.AddControllers();
builder.Services.AddScoped<TourScraperService>();
builder.Services.AddScoped<TourSeleniumService>();
builder.Services.AddScoped<TourImportService>(); // Thêm service để import vào database

// 3. Swagger để test API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 4. (Tùy chọn) CORS nếu frontend gọi API này
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// 5. Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Áp dụng migrations tự động trong môi trường development
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<TourDbContext>();
        dbContext.Database.Migrate();
    }
}

app.UseCors(); // nếu có dùng frontend
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers(); // ánh xạ Controller API

app.Run();