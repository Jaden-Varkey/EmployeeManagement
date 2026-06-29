/* ============================================================================
   SeedEmployees.sql  —  bulk test data for performance benchmarking
   ----------------------------------------------------------------------------
   Standalone script. NOT part of the build / app code. Run it in SSMS against
   the same database the app uses (EmployeeManagementDb).

   Inserts 10,000 employees with valid, app-conformant details:
     - FullName        : letters + spaces only
     - PhoneNumber     : canonical "+91 9XXXXXXXXX" form, UNIQUE (10-digit mobile)
     - Address         : >= 10 chars
     - Age             : 18..100
     - Gender          : Male / Female / Other
     - Department      : one of the 5 dropdown values (so row-grouping looks real)
     - AdmissionDate   : between 2018-01-01 and ~today
     - EmploymentStatus: one of the 6 dropdown values

   Id is an IDENTITY column and is intentionally omitted.
   ============================================================================ */

USE EmployeeManagementDb;
GO

SET NOCOUNT ON;

-- How many rows to generate. 10k is enough to feel the difference with paging
-- turned off (the grid renders every row). Bump to 50000 for a louder signal.
DECLARE @RowCount int = 10000;

;WITH Numbers AS (
    SELECT TOP (@RowCount)
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N
    FROM sys.all_objects a
    CROSS JOIN sys.all_objects b
)
INSERT INTO dbo.Employees
    (FullName, PhoneNumber, Address, Age, Gender, Department, AdmissionDate, EmploymentStatus)
SELECT
    -- FullName: first + last picked from small pools (letters/spaces only)
    CASE N % 10
        WHEN 0 THEN 'James'  WHEN 1 THEN 'Mary'    WHEN 2 THEN 'Robert' WHEN 3 THEN 'Linda'
        WHEN 4 THEN 'David'  WHEN 5 THEN 'Sarah'   WHEN 6 THEN 'Daniel' WHEN 7 THEN 'Priya'
        WHEN 8 THEN 'Arjun' ELSE 'Emily' END
    + ' ' +
    CASE (N / 10) % 10
        WHEN 0 THEN 'Smith'  WHEN 1 THEN 'Johnson' WHEN 2 THEN 'Williams' WHEN 3 THEN 'Brown'
        WHEN 4 THEN 'Jones'  WHEN 5 THEN 'Garcia'  WHEN 6 THEN 'Patel'    WHEN 7 THEN 'Khan'
        WHEN 8 THEN 'Nair'  ELSE 'Davis' END                                AS FullName,

    -- PhoneNumber: unique 10-digit Indian mobile in the app's stored "+CC NNN" form.
    -- 9000000001 .. 9000010000 — all 10 digits, all start with 9, all unique.
    '+91 ' + CAST(9000000000 + N AS varchar(15))                            AS PhoneNumber,

    -- Address: always >= 10 chars
    CAST(N AS varchar(10)) + ' Maple Avenue, Suite '
        + CAST((N % 200) + 1 AS varchar(5)) + ', Springfield'               AS Address,

    18 + (N % 83)                                                           AS Age,           -- 18..100

    CASE N % 3 WHEN 0 THEN 'Male' WHEN 1 THEN 'Female' ELSE 'Other' END     AS Gender,

    CASE N % 5
        WHEN 0 THEN 'Sales & Business'
        WHEN 1 THEN 'Client Relations & Operations'
        WHEN 2 THEN 'Software & IT'
        WHEN 3 THEN 'Marketing'
        ELSE 'HR' END                                                       AS Department,

    DATEADD(DAY, N % 3000, '2018-01-01')                                    AS AdmissionDate, -- 2018 .. ~2026

    CASE N % 6
        WHEN 0 THEN 'Full time'
        WHEN 1 THEN 'Part time'
        WHEN 2 THEN 'Freelance'
        WHEN 3 THEN 'Seasonal'
        WHEN 4 THEN 'Intern'
        ELSE 'Other' END                                                    AS EmploymentStatus
FROM Numbers;

PRINT CONCAT('Inserted rows. Total employees now: ',
             (SELECT COUNT(*) FROM dbo.Employees));
GO
