using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using EmployeeManagement.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Compresses response bodies (Using Gzip as requested) so large JSON
// payloads (e.g. GetEmployees for virtual-scroll mode) transfer smaller over the wire.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<GzipCompressionProvider>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Registering DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// === REDIS CACHING (comment this block out to fully disable Redis) ===
// Registers IDistributedCache backed by Redis. If this block is removed, IDistributedCache
// is simply not registered and EmployeeController falls back to querying the DB every time
// (its _cache dependency is optional/nullable).
builder.Services.AddStackExchangeRedisCache(o =>
{
    o.Configuration = builder.Configuration.GetConnectionString("Redis");
    o.InstanceName = "emp:";
});
// === END REDIS CACHING ===

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseResponseCompression();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Employee}/{action=Index}/{id?}");

app.Run();
