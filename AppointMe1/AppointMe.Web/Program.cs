using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Interface;
using AppointMe.Service.Implementation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddScoped<IServiceOfferingRepository, ServiceOfferingRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();



builder.Services.AddDefaultIdentity<AppointMeAppUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false; // Set to true in production
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 6;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();

builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<IHolidayService, HolidaysService>();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// If you want Dashboard as the default:
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();


//using AppointMe.Domain.DomainModels;
//using AppointMe.Domain.Identity;
//using AppointMe.Repository;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.EntityFrameworkCore; 

//var builder = WebApplication.CreateBuilder(args);

//// Add services to the container.
//var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
//builder.Services.AddDbContext<ApplicationDbContext>(options =>
//    options.UseSqlServer(connectionString));
//builder.Services.AddDatabaseDeveloperPageExceptionFilter();

//builder.Services.AddDefaultIdentity<AppointMeAppUser>(options => options.SignIn.RequireConfirmedAccount = true)
//    .AddEntityFrameworkStores<ApplicationDbContext>();
//builder.Services.AddControllersWithViews();

//var app = builder.Build();

//// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.UseMigrationsEndPoint();
//}
//else
//{
//    app.UseExceptionHandler("/Home/Error");
//    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
//    app.UseHsts();
//}

//app.UseHttpsRedirection();
//app.UseStaticFiles();

//app.UseRouting();

//app.UseAuthorization();

//app.MapControllerRoute(
//    name: "default",
//    pattern: "{controller=Home}/{action=Index}/{id?}");
//app.MapRazorPages();




////FOR TESTINT PURPOSES
//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
//    if (!db.Businesses.Any())
//    {
//        var business = new Business { Name = "Bianka Sposa Studio", Email = "info@biankasposa.com" };
//        db.Businesses.Add(business);

//        var service1 = new ServiceOffering { Business = business, Name = "Consultation", DurationMinutes = 30, Price = 0 };
//        var service2 = new ServiceOffering { Business = business, Name = "Fitting", DurationMinutes = 60, Price = 50 };
//        db.ServiceOfferings.AddRange(service1, service2);

//        var staff1 = new StaffMember { Business = business, FirstName = "Ana", LastName = "Petrova" };
//        var staff2 = new StaffMember { Business = business, FirstName = "Marko", LastName = "Stojanovski" };
//        db.StaffMembers.AddRange(staff1, staff2);

//        var customer1 = new Customer { Business = business, CustomerNumber = 1, FullName = "Elena Dimitrova", Email = "elena@mail.com" };
//        var customer2 = new Customer { Business = business, CustomerNumber = 2, FullName = "Ivana Koleva", Email = "ivana@mail.com" };
//        db.Customers.AddRange(customer1, customer2);

//        var appointment = new Appointment
//        {
//            Business = business,
//            ServiceOffering = service1,
//            StaffMember = staff1,
//            Customer = customer1,
//            StartUtc = DateTime.UtcNow.AddDays(1),
//            EndUtc = DateTime.UtcNow.AddDays(1).AddMinutes(30),
//            Status = AppointmentStatus.Scheduled,
//            BookedPrice = 0,
//            BookedDurationMinutes = 30
//        };
//        db.Appointments.Add(appointment);

//        db.SaveChanges();
//    }
//}

//app.Run();





