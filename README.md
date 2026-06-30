# Employee Management

A high-performance ASP.NET web application designed for viewing and managing large employee datasets efficiently.

## Overview
This application provides a fast and responsive user interface for displaying thousands of employee records without degrading browser performance. It achieves this by implementing custom **virtual scrolling (windowing)** to render only the data actively visible on the user's screen.

## Key Features
- **High-Performance Virtual Scrolling**: Smoothly scroll through 10,000+ employee records using an optimized DOM recycling pattern.
- **Department Grouping**: View and navigate employees categorically by their respective departments via an accordion interface.
- **Server-Side Data Delivery**: Data is dynamically fed to the frontend using optimized API endpoints and rendered via jQuery.
- **Responsive Layout**: Designed to provide an intuitive user experience across devices.

## Setup & Installation
1. Ensure the .NET SDK is installed.
2. Clone the repository.
3. Apply the database schema and seed data using `SeedEmployees.sql`.
4. Open `EmployeeManagement.slnx` in Visual Studio or use `dotnet run` from the `EmployeeManagement` directory.

## Architecture
For an in-depth, technical explanation of the codebase—including detailed breakdowns of the virtual scrolling implementation, windowing math, and DOM manipulation—please refer to the [Architecture Documentation](EmployeeManagement/ARCHITECTURE.md).
