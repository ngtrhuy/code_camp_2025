using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TouristApp.Services; // thay b?ng namespace th?t c?a b?n

var builder = WebApplication.CreateBuilder(args);

// 1. ??ng k� d?ch v? (Service + Controller)
builder.Services.AddControllers();
builder.Services.AddScoped<TourScraperService>(); // ??ng k� DI cho service
builder.Services.AddScoped<TourSeleniumService>();

// 2. Swagger ?? test API
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 3. (T�y ch?n) CORS n?u frontend g?i API n�y
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

app.UseCors(); // n?u c� d�ng frontend
app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers(); // �nh x? Controller API

app.Run();
