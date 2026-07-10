using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace EmployeeManagement.Controllers
{
    // Issues and refreshes the JWTs, and hosts the login page. Everything here is anonymous by
    // design — this is how an unauthenticated user gets a token in the first place.
    [AllowAnonymous]
    public class AuthController : Controller
    {
        private readonly IConfiguration _config;

        // In-memory refresh-token store (token -> expiry), same pattern as the reference API.
        // Lost on app restart and not shared across servers — fine for a single-instance demo.
        // Swap this for a DB/Redis table if you need persistence or multiple instances.
        private static readonly Dictionary<string, DateTime> _refreshTokens = new();

        public AuthController(IConfiguration config) => _config = config;

        private int AccessTokenMinutes => _config.GetValue<int?>("Jwt:AccessTokenMinutes") ?? 30;
        private double RefreshTokenHours => _config.GetValue<double?>("Jwt:RefreshTokenHours") ?? 8.0;

        // GET /Auth/Login — render the login form.
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return LocalRedirectOrHome(returnUrl);

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // POST /Auth/Login — verify credentials, then issue an access + refresh token as cookies.
        [HttpPost]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            // TODO: replace this hardcoded check with a real lookup (e.g. an EF Core Users table).
            if (request.Username == "jaden" && request.Password == "varkey")
            {
                var accessToken = GenerateAccessToken(request.Username);
                var refreshToken = Guid.NewGuid().ToString();
                _refreshTokens[refreshToken] = DateTime.UtcNow.AddHours(RefreshTokenHours);

                SetAccessCookie(accessToken);
                SetRefreshCookie(refreshToken);
                return Json(new { success = true });
            }

            return Unauthorized(new { success = false, message = "Invalid credentials" });
        }

        // POST /Auth/Refresh — swap a still-valid refresh token for a fresh access token.
        // Called by the page's AJAX layer when a data request comes back 401 (access expired).
        [HttpPost]
        public IActionResult Refresh()
        {
            if (!Request.Cookies.TryGetValue("refresh_token", out var refreshToken) ||
                string.IsNullOrEmpty(refreshToken))
            {
                return Unauthorized(new { success = false, message = "No refresh token." });
            }

            if (!_refreshTokens.TryGetValue(refreshToken, out var expiry))
            {
                // Unknown / tampered token.
                return Unauthorized(new { success = false, message = "Refresh token not recognized." });
            }

            if (DateTime.UtcNow > expiry)
            {
                _refreshTokens.Remove(refreshToken);
                ClearAuthCookies();
                return Unauthorized(new { success = false, message = "Refresh token expired." });
            }

            SetAccessCookie(GenerateAccessToken(User.Identity?.Name ?? "jaden"));
            return Json(new { success = true });
        }

        // GET /Auth/SilentRefresh — swap a still-valid refresh token for a fresh access token during a standard page load.
        [HttpGet]
        public IActionResult SilentRefresh(string? returnUrl)
        {
            if (!Request.Cookies.TryGetValue("refresh_token", out var refreshToken) ||
                string.IsNullOrEmpty(refreshToken))
            {
                return Redirect($"/Auth/Login?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
            }

            if (!_refreshTokens.TryGetValue(refreshToken, out var expiry) || DateTime.UtcNow > expiry)
            {
                _refreshTokens.Remove(refreshToken);
                ClearAuthCookies();
                return Redirect($"/Auth/Login?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
            }

            SetAccessCookie(GenerateAccessToken("jaden")); // Fallback to jaden since this is anonymous
            return LocalRedirectOrHome(returnUrl);
        }

        // POST /Auth/Logout — forget the refresh token and clear both cookies.
        [HttpPost]
        public IActionResult Logout()
        {
            if (Request.Cookies.TryGetValue("refresh_token", out var refreshToken) &&
                !string.IsNullOrEmpty(refreshToken))
            {
                _refreshTokens.Remove(refreshToken);
            }
            ClearAuthCookies();
            return Json(new { success = true });
        }

        // ---- helpers ----

        private string GenerateAccessToken(string username)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"],
                audience: _config["Jwt:Audience"],
                claims: new[] { new Claim(ClaimTypes.Name, username) },
                expires: DateTime.UtcNow.AddMinutes(AccessTokenMinutes),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // HttpOnly so JS can't read the token (mitigates XSS theft); Secure so it only travels over
        // HTTPS; SameSite=Strict so it isn't sent on cross-site requests. The cookie lifetime is set
        // to match the token lifetime.
        private void SetAccessCookie(string accessToken) =>
            Response.Cookies.Append("access_token", accessToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddMinutes(AccessTokenMinutes)
            });

        private void SetRefreshCookie(string refreshToken) =>
            Response.Cookies.Append("refresh_token", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddHours(RefreshTokenHours)
            });

        private void ClearAuthCookies()
        {
            Response.Cookies.Delete("access_token");
            Response.Cookies.Delete("refresh_token");
        }

        // Only ever redirect to a local path, so a crafted ?returnUrl can't bounce the user offsite.
        private IActionResult LocalRedirectOrHome(string? returnUrl) =>
            !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? Redirect(returnUrl)
                : Redirect("/");
    }

    // Holds the posted login JSON.
    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
