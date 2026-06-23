using Microsoft.AspNetCore.Mvc;
using EmployeeManagement.Data;
using EmployeeManagement.Models;
using System.Linq;
using PhoneNumbers;

namespace EmployeeManagement.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly ApplicationDbContext _context;

        // libphonenumber handles validation for every country, so we no longer maintain
        // per-country regex rules here. The client-side PHONE_RULES in Views/Employee/Index.cshtml
        // remain only as a fast pre-check; this server-side check is the source of truth.
        private static readonly PhoneNumberUtil PhoneUtil = PhoneNumberUtil.GetInstance();

        public EmployeeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Validates the national number against its country code using libphonenumber,
        // which knows the real numbering rules for every country.
        // Returns an error message, or null when the number is valid.
        private static string? ValidatePhone(string? countryCode, string? nationalNumber)
        {
            if (string.IsNullOrWhiteSpace(nationalNumber))
                return "Phone number is required.";

            if (string.IsNullOrWhiteSpace(countryCode))
                return "Country code is required.";

            try
            {
                var parsed = PhoneUtil.Parse(countryCode + nationalNumber, null);
                return PhoneUtil.IsValidNumber(parsed)
                    ? null
                    : $"The phone number is not valid for {countryCode}.";
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
            return View();
        }

        // Endpoint for jQuery DataTable to fetch data
        [HttpGet]
        public IActionResult GetEmployees(string searchName, int? searchId, DateTime? startDate, DateTime? endDate)
        {
            // AsQueryable allows us to dynamically build the SQL query before executing it
            var query = _context.Employees.AsQueryable();

            if (!string.IsNullOrEmpty(searchName))
            {
                query = query.Where(e => e.FullName.Contains(searchName));
            }

            if (searchId.HasValue)
            {
                query = query.Where(e => e.Id == searchId.Value);
            }

            if (startDate.HasValue)
            {
                query = query.Where(e => e.AdmissionDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(e => e.AdmissionDate <= endDate.Value);
            }

            // Executes the final built SQL query and gets the filtered list
            var data = query.ToList();
            return Json(new { data = data });
        }

        // New Endpoint for Name Autofill
        [HttpGet]
        public IActionResult AutocompleteNames(string term)
        {
            var names = _context.Employees
                .Where(e => e.FullName.Contains(term))
                .Select(e => e.FullName)
                .Distinct()              // Prevent duplicates if two people have the exact same name
                .Take(10)                // Suggesting only the top 10 matches to keep it fast
                .ToList();

            return Json(names);
        }

        
        [HttpGet]
        public IActionResult GetEmployeesByDepartment(string department)
        {
            var data = _context.Employees
                .Where(e => e.Department == department)
                .OrderBy(e => e.FullName)
                .ToList();

            return Json(new { data = data });
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
            var phoneError = ValidatePhone(model.CountryCode, model.PhoneNumber?.Trim());
            if (phoneError != null)
            {
                return Json(new { success = false, message = phoneError });
            }

            // Store the number in its full "+CC NNNNNNNNNN" form, then re-validate the model
            // (the PhoneNumber field is now in the format the model's regex expects).
            string fullPhoneNumber = $"{model.CountryCode} {model.PhoneNumber?.Trim()}".Trim();
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

                    return Json(new { success = true, message = "Employee details saved successfully!" });
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException)
                {
                    // This catches the CHECK constraint or any other database-level rejection
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
                var phoneError = ValidatePhone(code, number);
                if (phoneError != null)
                {
                    errors.Add($"Row {i + 1}: {phoneError}");
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

                return Json(new { success = true, message = $"{employees.Count} employees saved successfully!" });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return Json(new { success = false, message = "The database rejected the save. Please verify all details are valid and every phone number is unique." });
            }
        }
    }
}