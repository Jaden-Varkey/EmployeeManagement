using Microsoft.EntityFrameworkCore;
using EmployeeManagement.Models;

namespace EmployeeManagement.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Employee> Employees { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Phone number must be bounded so it can be indexed (nvarchar(max) cannot be),
            // and unique at the database level so duplicates are impossible even under
            // concurrent saves. This mirrors the unique index already present on the
            // live database (UQ__Employee...) so the constraint is reproducible from code.
            modelBuilder.Entity<Employee>(entity =>
            {
                entity.Property(e => e.PhoneNumber).HasMaxLength(20);
                entity.HasIndex(e => e.PhoneNumber).IsUnique();
            });
        }
    }
}