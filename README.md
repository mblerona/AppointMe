# AppointMe

**Advanced Project – Integrated Systems**  
A multi-tenant appointment management web application built with **.NET 8 MVC** and **Onion Architecture**.

- 🌐 Hosted application: https://appointmeweb20260112125147.azurewebsites.net

---

## 📌 Overview

**AppointMe** is a web-based, multi-tenant appointment scheduling system designed to help businesses manage customers, services, appointments, and invoices efficiently.  
Each registered business operates as an isolated tenant, ensuring complete data separation while sharing the same platform.

The application emphasizes **clean architecture**, **scalability**, and **real-world applicability**, making it suitable both as an academic advanced project and as a SaaS-ready foundation.

---

##  Key Features

- Multi-tenant architecture with strict data isolation
- Business-defined working days, working hours, and appointment duration
- Appointment scheduling with availability and overlap validation
- Optional service management with price snapshot at booking time
- Optional automatic invoice generation
- Email notifications for appointments and invoices
- Calendar (`.ics`) file generation for external calendar tools
- Public holiday validation via external API integration

---

##  Multi-Tenancy

- Each registered user represents a **tenant (business owner)**
- All core entities (customers, services, appointments, invoices) are tenant-scoped
- Data access is always filtered by tenant context
- Ensures secure isolation between businesses while allowing horizontal scaling

---

##  Configurable Business Logic

AppointMe adapts to different business workflows:

- Services module can be enabled or disabled
- Invoicing module can be enabled or disabled
- Appointments can exist with or without services
- Invoices are generated only when enabled

---

##  Architecture

The application follows the **Onion Architecture** pattern:

- **Domain Layer** – Core business entities and rules  
- **Repository Layer** – Data access abstractions and implementations  
- **Service Layer** – Business logic and orchestration  
- **Web Layer** – ASP.NET MVC controllers and views  

This separation of concerns improves maintainability, testability, and long-term scalability.

---

##  User Flow

### Registration & Initial Setup
- Registering a user creates a new **tenant**
- After first login, the user is redirected to **Business Settings**
- Business configuration is mandatory and defines core application behavior

### Application Usage
1. *(Optional)* Define services and prices  
2. Add customers  
3. Schedule appointments (validated against business rules and availability)  
4. *(Optional)* Generate and email invoices  

---

##  Email Integration Notice

To test email functionality (appointment confirmations and invoices),  
**customers must have a valid, existing email address**.

---

##  Tech Stack

- ASP.NET Core 8 MVC  
- Entity Framework Core  
- Onion Architecture  
- SQL Server  
- External Public Holidays API  
- SMTP Email Integration  

---

##  Testing Overview

The application is validated using a **multi-layered testing strategy** covering business logic, data persistence, and web/API behavior.  
A total of **111 automated tests** were implemented: **51 unit tests**, **38 integration tests**, and **22 web/API tests**.

- **Unit tests** validate core service logic (appointments, invoices, holidays, email notifications) with isolated dependencies  
- **Integration tests** verify Entity Framework Core mappings, repository queries, and strict tenant-based data isolation  
- **Web/API tests** confirm correct controller behavior, routing, and service orchestration  

Additional quality evaluation was performed using **code coverage analysis** and **mutation testing (Stryker.NET)**, achieving approximately **40% mutation score**, demonstrating effective fault detection in critical business logic.

**Testing technologies used:**  
xUnit, FluentAssertions, Entity Framework Core InMemory, ASP.NET WebApplicationFactory, ReportGenerator, Stryker.NET

---

##  Future Improvements

- Client self-service portal  
- Online payments integration  
- Advanced reporting and analytics  
- Extended staff and role management  

---

##  Author

**AppointMe**  
Blerona Mulladauti  
Integrated Systems – Advanced Project

---

## ⚙️ Configuration

The application requires an `appsettings.json` file which is **not committed** to the repository.

Use the following template to create your own configuration:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "YOUR_CONNECTION_STRING"
  },
  "EmailSettings": {
    "SmtpServer": "smtp.example.com",
    "SmtpServerPort": 587,
    "EnableSsl": true,
    "EmailDisplayName": "AppointMe",
    "SenderName": "AppointMe",
    "SmtpUserName": "YOUR_EMAIL",
    "SmtpPassword": "YOUR_PASSWORD"
  }
}
