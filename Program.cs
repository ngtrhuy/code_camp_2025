using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouristApp.Services; // thay b?ng namespace th?t c?a b?n

var builder = WebApplication.CreateBuilder(args);

// 1. ??ng ký d?ch v? (Service + Controller)
builder.Services.AddControllers();
builder.Services.AddScoped<TourScraperService>(); // ??ng ký DI cho service
builder.Services.AddScoped<TourSeleniumService>();

// 2. Swagger ?? test API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 3. (Tùy ch?n) CORS n?u frontend g?i API này
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// 4. Middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(); // n?u có dùng frontend
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers(); // ánh x? Controller API

app.Run();
