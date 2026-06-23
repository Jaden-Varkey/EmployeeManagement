using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EmployeeManagement.Models
{
    public class Employee
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Full Name is required")]
        [RegularExpression(@"^[a-zA-Z\s]+$", ErrorMessage = "Full Name can only contain letters and spaces.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; }

        [NotMapped]
        public string CountryCode { get; set; }

        [Required(ErrorMessage = "Phone Number is required")]
        [Display(Name = "Phone Number")]
        [RegularExpression(@"^\+\d{1,3}\s\d{10}$", ErrorMessage = "Phone number must include a country code and 10 digits.")]
        public string PhoneNumber { get; set; }

        [Required(ErrorMessage = "Address is required")]
        [MinLength(10, ErrorMessage = "Address must be at least 10 characters long.")]
        public string Address { get; set; }

        [Required(ErrorMessage = "Age is required")]
        [Range(18, 100, ErrorMessage = "Age must be between 18 and 100")]
        public int Age { get; set; }

        [Required(ErrorMessage = "Gender is required")]
        public string Gender { get; set; }

        [Required(ErrorMessage = "Department is required")]
        public string Department { get; set; }

        [Required(ErrorMessage = "Admission Date is required")]
        [DataType(DataType.Date)]
        [Display(Name = "Admission Date")]
        public DateTime AdmissionDate { get; set; }

        [Required(ErrorMessage = "Employment Status is required")]
        [Display(Name = "Employment Status")]
        public string EmploymentStatus { get; set; }
    }
}