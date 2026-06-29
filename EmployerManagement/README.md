# Employee Management System - Architecture Documentation

This document outlines the full-stack architecture for the Employee Management application. It explains how the database, backend C# code, and frontend UI communicate to save and display records.

---

## 1. The Database Layer (SQL Server)
The SQL database acts as the absolute foundation of the application and the final gatekeeper for data integrity.

* **`CREATE TABLE Employees`**: Defines the exact structure of the data on the hard drive.
* **`Id INT IDENTITY(1,1) PRIMARY KEY`**: `IDENTITY(1,1)` tells SQL Server to automatically generate this number (1, 2, 3...). `PRIMARY KEY` makes it the unique identifier for the row.
* **`NVARCHAR` vs `VARCHAR`**: The "N" stands for National. It allows the database to store special international characters (like letters with accents). The number (e.g., 250) represents the maximum character limit.
* **`CHECK (...)` constraints**: These are the ultimate security rules. Even if the C# code or the frontend HTML is bypassed, SQL Server will physically refuse to save a row if the `PhoneNumber` doesn't match the exact rules (e.g., allowing only numbers, spaces, and the `+` sign for country codes).

---

## 2. The Backend Data Layer (C# Models & Entity Framework)
Instead of writing raw SQL queries (like `INSERT INTO Employees...`) inside the application, this project uses an Object-Relational Mapper (ORM) called **Entity Framework Core (EF Core)** to translate C# objects directly into SQL commands.

* **The Model (`Employee.cs`)**: The C# blueprint of the SQL table. Every property here maps directly to a column in SQL.
    * **Data Annotations (`[Required]`, `[MinLength]`, `[RegularExpression]`)**: These attributes define the business rules. C# checks these rules before attempting to communicate with the SQL database.
* **The DbContext (`ApplicationDbContext.cs`)**: The "bridge" between the C# code and SQL Server. EF Core is the library/framework that translates C# LINQ to raw SQL queries.
    * `DbSet<Employee> Employees`: Instructs EF Core to map the `Employee` C# class to the `Employees` SQL table.
* **`Program.cs`**: On application startup, `builder.Services.AddDbContext(...)` reads the database connection string and officially opens the bridge for the rest of the application to use.

---

## 3. The Brains (The `EmployeeController.cs`)
* **The Controller** manages traffic. It waits for HTTP requests from the web browser, processes data, interacts with the database via EF Core, and sends responses back.

* **`GetEmployees()`**: Triggered when the web page requests table data. `_context.Employees.ToList()` is executed by EF Core, which generates a `SELECT * FROM Employees` query, retrieves the data, converts it into a list of C# objects, and returns it to the browser as JSON.
* **`SaveEmployee(Employee model)`**: The endpoint that handles form submissions.
    * **Model Binding**: ASP.NET Core automatically parses the incoming AJAX form data, matches the input names (like "FullName" or "Age"), and builds a fully populated `Employee` object.
    * **`ModelState.IsValid`**: Triggers the Data Annotations in `Employee.cs`. If a validation rule fails (e.g., numbers in the name), it skips the database save entirely and returns the specific error messages.
    * **`try...catch`**: If C# passes the data but SQL Server rejects it (due to a `CHECK` constraint or `UNIQUE` violation), the `catch` block intercepts the crash and returns a clean JSON error message instead of breaking the server.

---

## 4. The Frontend (HTML & JavaScript in `Index.cshtml`)
The user interface combines standard HTML with Bootstrap for styling, DataTables for the data grid, and SweetAlert2 for notifications.

* **The HTML Form**: Utilizes standard inputs with built-in HTML5 validation (like `minlength="5"` and `required`). This provides the first line of defense, allowing the browser to stop the user before making a server request.
* **DataTables (`$('#EmployeeTable').DataTable(...)`)**: A jQuery plugin that converts a standard HTML table into an interactive grid. The `"ajax"` configuration instructs the table to call the Controller's `GetEmployees()` method to dynamically fetch data.
* **jQuery AJAX (`$.ajax(...)`)**: Handles the asynchronous form submission:
    1.  `$('#EmployeeForm').serialize()` gathers all input field values into a URL-encoded string.
    2.  `type: 'POST'` sends that payload to the `SaveEmployee` Controller action in the background, preventing a full page reload.
    3.  `success: function(response)` listens for the Controller's JSON response. If successful, it triggers a green SweetAlert, resets the form, and calls `table.ajax.reload()` to update the grid with the new database entry seamlessly.


    To enable/disable server-side pagination, set "const ENABLE_PAGINATION = false" or "const ENABLE_DEPT_PAGING = false" in index.cshtml (View)
    To enable/disable Redis, set "EnableRedisCache = false" in EmployeeController.cs (Controller) (+ restart)