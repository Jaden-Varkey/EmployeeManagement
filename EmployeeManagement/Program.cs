using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using EmployeeManagement.Data;
using System.Text;

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

// === JWT AUTHENTICATION (access + refresh tokens) ===
// Ported from the reference API. The access token is validated here on every request; it is
// read from the "access_token" cookie (see OnMessageReceived) because a normal browser page
// navigation cannot send an Authorization header — that is what lets us gate the PAGE, not just
// the AJAX endpoints. All the knobs (issuer/audience/key/lifetimes) live in appsettings' "Jwt"
// section so nothing is hardcoded in source.
var jwt = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,       // expiry is what triggers the 401 -> refresh flow
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!)),
            ClockSkew = TimeSpan.Zero      // no grace period, so expiry is exact
        };

        options.Events = new JwtBearerEvents
        {
            // Page navigations can't set an Authorization header, so pull the JWT from the cookie.
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) &&
                    context.Request.Cookies.TryGetValue("access_token", out var cookieToken))
                {
                    context.Token = cookieToken;
                }
                return Task.CompletedTask;
            },

            // No valid token: redirect a real browser navigation to the silent refresh page
            // so we can seamlessly get a new access token, but leave the 401 in place for AJAX/data calls 
            // so the client JS wrapper can refresh + retry.
            OnChallenge = context =>
            {
                var req = context.Request;
                bool isAjax = req.Headers["X-Requested-With"] == "XMLHttpRequest";
                bool wantsHtml = req.Headers["Accept"].ToString().Contains("text/html");
                if (!isAjax && wantsHtml)
                {
                    context.HandleResponse(); // suppress the default 401 body
                    var returnUrl = Uri.EscapeDataString(req.Path + req.QueryString);
                    context.Response.Redirect($"/Auth/SilentRefresh?returnUrl={returnUrl}");
                }
                return Task.CompletedTask;
            }
        };
    });
// === END JWT AUTHENTICATION ===

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
app.UseAuthentication(); // MUST be before UseAuthorization so the cookie/JWT is read first
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Employee}/{action=Index}/{id?}");

app.Run();
