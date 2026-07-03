using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using EmployeeManagement.Data;
using EmployeeManagement.Models;
using System.Linq;
using PhoneNumbers;

namespace EmployeeManagement.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly ApplicationDbContext _context;

        // === REDIS CACHING ===
        // Distributed (Redis) cache for GetEmployees responses. Nullable on purpose: if the
        // AddStackExchangeRedisCache block in Program.cs is commented out, this is null and
        // every read falls straight through to EF — no other code changes needed.
        private readonly IDistributedCache? _cache;

        // Hard off-switch for caching (flip to false and restart to disable entirely).
        // Live off-switch with no restart: append ?cache=false to the GetEmployees request.
        private const bool EnableRedisCache = true;
        // === END REDIS CACHING ===

        // libphonenumber handles validation for every country. The view runs the same kind of
        // check client-side (validatePhoneClient, via libphonenumber-js) purely for fast UX
        // feedback; this server-side check is the source of truth and can't be bypassed.
        private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

        // The Web API automatically injects our EF Core database bridge here.
        // IDistributedCache is optional so the app still runs if Redis is disabled.
        public EmployeeController(ApplicationDbContext context, IDistributedCache? cache = null)
        {
            _context = context;
            _cache = cache;
        }

        // === REDIS CACHING ===
        // Resilient cache access: if Redis is configured but unreachable, every call here must
        // behave like a cache MISS (read) or a no-op (write) instead of throwing. Without this,
        // a Redis outage turns every read endpoint into an HTTP 500 — the directory and search
        // appear broken even though the database is fine. (Note: this is different from removing
        // the AddStackExchangeRedisCache block in Program.cs, which makes _cache null; here _cache
        // is non-null but the server behind it is down.)
        private string? CacheGet(string key)
        {
            if (_cache == null) return null;
            try { return _cache.GetString(key); }
            catch { return null; } // Redis down → treat as a miss, fall through to the DB
        }

        private void CacheSet(string key, string value, DistributedCacheEntryOptions? opts = null)
        {
            if (_cache == null) return;
            try { _cache.SetString(key, value, opts ?? new DistributedCacheEntryOptions()); }
            catch { /* Redis down → skip caching, the DB result is still returned */ }
        }

        // Cache invalidation by version number. Every cache key embeds the current version;
        // a save bumps the version, which orphans all previously cached pages so the next
        // request rebuilds from the DB exactly once (then re-caches). A 5-minute TTL is a
        // safety net in case a bump is ever missed.
        private long GetCacheVersion()
        {
            var v = CacheGet("employees:version");
            return long.TryParse(v, out var n) ? n : 0;
        }

        private void BumpCacheVersion()
        {
            CacheSet("employees:version", (GetCacheVersion() + 1).ToString());
        }

        // camelCase / web defaults so the JSON matches what the DataTable columns expect
        // (fullName, phoneNumber, ...). MVC's Json() helper does this automatically, but here
        // we serialize manually (to cache the string), so we must opt in explicitly — otherwise
        // System.Text.Json emits PascalCase and DataTables can't find the columns.
        private static readonly System.Text.Json.JsonSerializerOptions JsonOpts =
            new(System.Text.Json.JsonSerializerDefaults.Web);

        // Splices the request's own "draw" counter onto a cached response body. DataTables
        // uses draw to discard out-of-order responses, so it must echo the *incoming* value
        // and therefore can never be part of the cached payload. inner always starts with '{'.
        private static string WithDraw(string inner, int draw) =>
            "{\"draw\":" + draw + "," + inner.Substring(1);
        // === END REDIS CACHING ===

        // Validates the national number against its country code using libphonenumber,
        // which knows the real numbering rules for every country.
        // Returns an error message, or null when the number is valid.
        private static string? ValidatePhone(string? countryCode, string? nationalNumber,
            out string normalizedCode, out string normalizedNational)
        {
            normalizedCode = (countryCode ?? string.Empty).Trim();
            normalizedNational = (nationalNumber ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(nationalNumber))
                return "Phone number is required.";

            if (string.IsNullOrWhiteSpace(countryCode))
                return "Country code is required.";

            try
            {
                var parsed = PhoneUtil.Parse(countryCode + nationalNumber, null);
                if (!PhoneUtil.IsValidNumber(parsed))
                    return $"The phone number is not valid for {countryCode}.";

                // IsValidNumber accepts every assigned number type (including premium-rate
                // and toll-free). For an employee contact, only allow mobile/landline.
                var type = PhoneUtil.GetNumberType(parsed);
                if (type != PhoneNumberType.MOBILE &&
                    type != PhoneNumberType.FIXED_LINE &&
                    type != PhoneNumberType.FIXED_LINE_OR_MOBILE)
                {
                    return "Please enter a mobile or landline number (premium-rate, toll-free, and special-service numbers aren't allowed).";
                }

                // Re-derive the stored value from libphonenumber's canonical E.164 form. This
                // drops any trunk prefix / leading zero the user typed (e.g. "+971 0504116432")
                // so the national number always has the correct per-country length
                // ("+971 504116432"). Without this, the 10-digit trunk form was being stored
                // verbatim even though only the 9-digit national number is valid.
                var e164 = PhoneUtil.Format(parsed, PhoneNumberFormat.E164); // "+971504116432"
                normalizedCode = "+" + parsed.CountryCode;                   // "+971"
                normalizedNational = e164.Substring(normalizedCode.Length);  // "504116432"
                return null;
            }
            catch (NumberParseException)
            {
                return "The phone number could not be parsed. Please check the country code and number.";
            }
        }

        // Splits a stored phone value ("+91 6123456789") into its country code and national number.
        private static (string Code, string Number) SplitPhone(Employee e)
        {
            var phone = (e.PhoneNumber ?? string.Empty).Trim();
            var parts = phone.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
                return (parts[0], parts[1]);

            // Fall back to the separate CountryCode field if the number wasn't space-delimited.
            return (e.CountryCode ?? string.Empty, phone);
        }

        // Rendering the main management page
        public IActionResult Index()
        {
            // Hand the view the full set of dialing codes libphonenumber knows about, so the
            // country dropdown stays in lock-step with what the server-side validator accepts.
            ViewBag.CountryCodeOptions = BuildCountryCodeOptions();
            return View();
        }

        // Builds the <option> list for the country-code dropdown: every distinct dialing code
        // libphonenumber supports, sorted numerically, e.g. <option value="+91">+91</option>.
        // Regions that share a code (US/CA/... all +1) collapse to a single entry.
        private const string DefaultCountryCode = "+91";

        private static string BuildCountryCodeOptions()
        {
            var codes = PhoneUtil.GetSupportedRegions()
                .Select(region => PhoneUtil.GetCountryCodeForRegion(region))
                .Where(code => code > 0)
                .Distinct()
                .OrderBy(code => code)
                .ToList();

            var sb = new System.Text.StringBuilder();
            foreach (var code in codes)
            {
                var value = "+" + code;
                var selected = value == DefaultCountryCode ? " selected" : string.Empty;
                sb.Append($"<option value=\"{value}\"{selected}>{value}</option>");
            }
            return sb.ToString();
        }

        // Shared search/filter logic used by every read endpoint (GetEmployees,
        // GetDepartmentSummary, GetEmployeesByDepartment) so they always filter identically.
        private static IQueryable<Employee> ApplyFilters(IQueryable<Employee> q,
            string searchName, int? searchId, DateTime? startDate, DateTime? endDate)
        {
            if (!string.IsNullOrEmpty(searchName)) q = q.Where(e => e.FullName.Contains(searchName));
            if (searchId.HasValue)                 q = q.Where(e => e.Id == searchId.Value);
            if (startDate.HasValue)                q = q.Where(e => e.AdmissionDate >= startDate.Value);
            if (endDate.HasValue)                  q = q.Where(e => e.AdmissionDate <= endDate.Value);
            return q;
        }

        // Endpoint for jQuery DataTable to fetch data.
        //
        // Works in two modes off the same code path:
        //   - PAGINATION OFF: the view (serverSide:false) sends no "length", so we return the
        //     whole filtered list as { data: [...] } (DataTables ignores the extra fields).
        //   - PAGINATION ON:  the view (serverSide:true) sends draw/start/length, so we return
        //     one page plus the { draw, recordsTotal, recordsFiltered, data } DataTables needs.
        //
        // Redis (when enabled) caches the serialized body, so a repeat request skips the SQL
        // query AND the JSON serialization entirely.
        [HttpGet]
        public IActionResult GetEmployees(string searchName, int? searchId, DateTime? startDate,
            DateTime? endDate, int? draw, int? start, int? length, bool cache = true)
        {
            // The client only sends a positive "length" in server-side (paginated) mode.
            bool paginate = length.HasValue && length.Value > 0;

            // === REDIS CACHING ===
            // Compute the key (and touch Redis) ONLY when caching is on, so ?cache=false is a
            // genuine bypass that never contacts Redis at all.
            bool useCache = EnableRedisCache && cache && _cache != null;
            string? cacheKey = null;
            if (useCache)
            {
                cacheKey = $"employees:v{GetCacheVersion()}:p{paginate}:{start}:{length}:" +
                           $"{searchName}:{searchId}:{startDate:o}:{endDate:o}";
                var cached = CacheGet(cacheKey);
                if (cached != null)
                    return Content(WithDraw(cached, draw ?? 0), "application/json"); // HIT: no SQL, no serialize
            }
            // === END REDIS CACHING ===

            // AsQueryable allows us to dynamically build the SQL query before executing it
            var query = _context.Employees.AsQueryable();

            // recordsTotal (unfiltered count) is only needed by DataTables in paginated mode.
            int recordsTotal = paginate ? query.Count() : 0;

            query = ApplyFilters(query, searchName, searchId, startDate, endDate);

            int recordsFiltered = paginate ? query.Count() : 0;

            // Stable ordering so paging is deterministic across requests.
            query = query.OrderBy(e => e.FullName);

            // In paginated mode pull only the requested window; otherwise the full list.
            var data = paginate
                ? query.Skip(start ?? 0).Take(length!.Value).ToList()
                : query.ToList();

            if (!paginate)
            {
                recordsTotal = recordsFiltered = data.Count;
            }

            // Body WITHOUT draw (draw is per-request and must never be cached — see WithDraw).
            string inner = System.Text.Json.JsonSerializer.Serialize(
                new { recordsTotal, recordsFiltered, data }, JsonOpts);

            // === REDIS CACHING ===
            if (useCache)
            {
                CacheSet(cacheKey!, inner, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            // === END REDIS CACHING ===

            return Content(WithDraw(inner, draw ?? 0), "application/json");
        }

        // Lazy-grouping endpoint #1: the department folders. Returns just one row per
        // department with its (filtered) headcount — a cheap GROUP BY — so the directory's
        // initial load is a few hundred bytes instead of the whole 50k-row table.
        [HttpGet]
        public IActionResult GetDepartmentSummary(string searchName, int? searchId,
            DateTime? startDate, DateTime? endDate, bool cache = true)
        {
            // === REDIS CACHING ===
            bool useCache = EnableRedisCache && cache && _cache != null;
            string? cacheKey = null;
            if (useCache)
            {
                cacheKey = $"deptsummary:v{GetCacheVersion()}:{searchName}:{searchId}:{startDate:o}:{endDate:o}";
                var hit = CacheGet(cacheKey);
                if (hit != null) return Content(hit, "application/json");
            }
            // === END REDIS CACHING ===

            var q = ApplyFilters(_context.Employees.AsQueryable(), searchName, searchId, startDate, endDate);
            var summary = q.GroupBy(e => e.Department)
                           .Select(g => new { department = g.Key, count = g.Count() })
                           .OrderBy(x => x.department)
                           .ToList();

            string json = System.Text.Json.JsonSerializer.Serialize(summary, JsonOpts);

            // === REDIS CACHING ===
            if (useCache)
            {
                CacheSet(cacheKey!, json, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            // === END REDIS CACHING ===

            return Content(json, "application/json");
        }

        // Lazy-grouping endpoint #2: the employees inside one department, loaded on demand when
        // its folder is expanded. Honours the same search filters and is Redis-cached.
        // OPTION 2 (in-department paging): when start/length are supplied, returns just that page
        // plus the filtered total, so a 10k-person department renders 25 rows at a time instead
        // of all at once. With no length, it still returns the whole department (paging off).
        [HttpGet]
        public IActionResult GetEmployeesByDepartment(string department, string searchName,
            int? searchId, DateTime? startDate, DateTime? endDate, int? start, int? length, bool cache = true)
        {
            bool paginate = length.HasValue && length.Value > 0;

            // === REDIS CACHING ===
            bool useCache = EnableRedisCache && cache && _cache != null;
            string? cacheKey = null;
            if (useCache)
            {
                cacheKey = $"deptrows:v{GetCacheVersion()}:{department}:{start}:{length}:{searchName}:{searchId}:{startDate:o}:{endDate:o}";
                var hit = CacheGet(cacheKey);
                if (hit != null) return Content(hit, "application/json");
            }
            // === END REDIS CACHING ===

            var q = ApplyFilters(_context.Employees.Where(e => e.Department == department),
                                 searchName, searchId, startDate, endDate)
                    .OrderBy(e => e.FullName);

            // total = filtered count for the pager; only needed when paginating.
            int total = paginate ? q.Count() : 0;
            var rows = paginate ? q.Skip(start ?? 0).Take(length!.Value).ToList() : q.ToList();
            if (!paginate) total = rows.Count;

            string json = System.Text.Json.JsonSerializer.Serialize(new { data = rows, total }, JsonOpts);

            // === REDIS CACHING ===
            if (useCache)
            {
                CacheSet(cacheKey!, json, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
            }
            // === END REDIS CACHING ===

            return Content(json, "application/json");
        }

        // AJAX Action to handle the form submission with custom validation
        [HttpPost]
        public IActionResult SaveEmployee(Employee model)
        {
            if (!string.IsNullOrEmpty(model.Address))
            {
                model.Address = model.Address.Trim();
            }

            // Validate the phone against its country rules BEFORE touching the database,
            // so the user gets the real reason instead of a generic DB rejection.
            var phoneError = ValidatePhone(model.CountryCode, model.PhoneNumber?.Trim(),
                out var normalizedCode, out var normalizedNational);
            if (phoneError != null)
            {
                return Json(new { success = false, message = phoneError });
            }

            // Store the canonical "+CC NNN..." form (trunk zeros stripped, correct length),
            // then re-validate the model (the PhoneNumber field is now in the format the
            // model's regex expects).
            string fullPhoneNumber = $"{normalizedCode} {normalizedNational}";
            model.CountryCode = normalizedCode;
            model.PhoneNumber = fullPhoneNumber;
            ModelState.Clear();
            TryValidateModel(model);

            if (ModelState.IsValid)
            {
                try
                {
                    // Check if phone number is unique
                    bool phoneExists = _context.Employees.Any(e => e.PhoneNumber == fullPhoneNumber);
                    if (phoneExists)
                    {
                        return Json(new { success = false, message = "This phone number is already registered to another employee." });
                    }

                    _context.Employees.Add(model);
                    _context.SaveChanges(); // If the DB rejects it, it immediately jumps to the catch block below

                    BumpCacheVersion(); // === REDIS CACHING === invalidate cached lists after a write

                    return Json(new { success = true, message = "Employee details saved successfully!" });
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException)
                {
                    // Catches any database-level rejection (e.g. the unique phone index).
                    return Json(new { success = false, message = "The database rejected the save. Please verify all details are valid and the phone number is unique." });
                }
            }

            var errorMessages = ModelState.Values
                                          .SelectMany(v => v.Errors)
                                          .Select(e => e.ErrorMessage)
                                          .ToList();

            // Combine them with HTML line breaks so swal displays them nicely
            string combinedErrors = string.Join("<br/>", errorMessages);

            return Json(new { success = false, message = combinedErrors });
        }

        [HttpPost]
        public IActionResult SaveMultipleEmployees([FromBody] List<Employee> employees)
        {
            // 1. Check if the array is empty
            if (employees == null || employees.Count == 0)
            {
                return Json(new { success = false, message = "No data provided to save." });
            }

            var errors = new List<string>();

            // 2. Per-row phone validation (correct, country-specific reason for each failure).
            for (int i = 0; i < employees.Count; i++)
            {
                var emp = employees[i];
                if (!string.IsNullOrEmpty(emp.Address))
                {
                    emp.Address = emp.Address.Trim();
                }

                var (code, number) = SplitPhone(emp);
                var phoneError = ValidatePhone(code, number, out var normalizedCode, out var normalizedNational);
                if (phoneError != null)
                {
                    errors.Add($"Row {i + 1}: {phoneError}");
                }
                else
                {
                    // Overwrite with the canonical form so the duplicate checks below and the
                    // DB unique index compare normalized numbers, not the trunk-prefixed text
                    // the user happened to type.
                    emp.CountryCode = normalizedCode;
                    emp.PhoneNumber = $"{normalizedCode} {normalizedNational}";
                }
            }

            // 3. Duplicate phone numbers WITHIN the submitted batch.
            var dupesInBatch = employees
                .Select((e, idx) => new { Phone = (e.PhoneNumber ?? string.Empty).Trim(), Row = idx + 1 })
                .Where(x => !string.IsNullOrEmpty(x.Phone))
                .GroupBy(x => x.Phone)
                .Where(g => g.Count() > 1);
            foreach (var g in dupesInBatch)
            {
                errors.Add($"Duplicate phone number '{g.Key}' is used in rows {string.Join(", ", g.Select(x => x.Row))}. Each employee must have a unique phone number.");
            }

            // 4. Duplicate phone numbers against employees already in the database.
            var batchPhones = employees.Select(e => (e.PhoneNumber ?? string.Empty).Trim()).ToList();
            var alreadyRegistered = _context.Employees
                .Where(e => batchPhones.Contains(e.PhoneNumber))
                .Select(e => e.PhoneNumber)
                .Distinct()
                .ToList();
            foreach (var phone in alreadyRegistered)
            {
                errors.Add($"Phone number '{phone}' is already registered to another employee.");
            }

            // 5. Remaining model validation (name, age, address, dropdowns, dates, ...).
            if (!ModelState.IsValid)
            {
                errors.AddRange(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            }

            if (errors.Any())
            {
                return Json(new { success = false, message = string.Join("<br/>", errors.Distinct()) });
            }

            try
            {
                // AddRange is highly optimized for saving arrays to SQL Server
                _context.Employees.AddRange(employees);
                _context.SaveChanges();

                BumpCacheVersion(); // === REDIS CACHING === invalidate cached lists after a write

                return Json(new { success = true, message = $"{employees.Count} employees saved successfully!" });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return Json(new { success = false, message = "The database rejected the save. Please verify all details are valid and every phone number is unique." });
            }
        }
    }
}