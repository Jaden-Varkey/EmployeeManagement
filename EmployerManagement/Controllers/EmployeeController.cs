using Microsoft.AspNetCore.Mvc;
using EmployeeManagement.Data;

namespace EmployeeManagement.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public EmployeeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Blueprint stub — the management page and data endpoints are implemented in later commits.
        public IActionResult Index()
        {
            return View();
        }
    }
}
