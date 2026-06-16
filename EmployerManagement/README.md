# Employee Management System

An ASP.NET Core MVC web application for managing employee records — bulk entry plus a
searchable, filterable employee directory — backed by SQL Server via Entity Framework Core.

This is the initial project scaffold (blueprint). Features are built out in subsequent commits.

## Planned architecture

* **Model** (`Employee.cs`) — the employee record and its validation rules.
* **Data layer** (`Data/ApplicationDbContext.cs`) — EF Core bridge to SQL Server.
* **Controller** (`Controllers/EmployeeController.cs`) — request handling, querying, and saving.
* **View** (`Views/Employee/Index.cshtml`) — the management UI (entry grid + directory).

## Configuration

The database connection string is read from `ConnectionStrings:DefaultConnection`.
For local development, override it with .NET user-secrets so no real connection string is
committed:

```
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<your connection string>"
```

## Running

```
dotnet run --project EmployerManagement/EmployeeManagement.csproj
```
