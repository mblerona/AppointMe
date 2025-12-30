using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Repository.Data
{
    public class ApplicationDbContext : IdentityDbContext<AppointMeAppUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<Customer> Customers { get; set; }
        public DbSet<Appointment> Appointments { get; set; }

        public DbSet<Business> Businesses { get; set; }
        public DbSet<ServiceOffering> ServiceOfferings { get; set; }
        public DbSet<ServiceCategory> ServiceCategories { get; set; }
        public DbSet<AppointmentServiceModel> AppointmentServices { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceLine> InvoiceLines { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ==================== CUSTOMER ====================
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.HasKey(c => c.Id);

                entity.Property(c => c.Id).HasDefaultValueSql("NEWID()");
                entity.Property(c => c.TenantId).IsRequired();

                entity.Property(c => c.FirstName).IsRequired().HasMaxLength(50);
                entity.Property(c => c.LastName).IsRequired().HasMaxLength(50);
                entity.Property(c => c.Email).IsRequired().HasMaxLength(256);

                entity.Property(c => c.PhoneNumber).IsRequired().HasMaxLength(20);
                entity.Property(c => c.SecondPhoneNumber).HasMaxLength(20);

                entity.Property(c => c.State).IsRequired().HasMaxLength(50);
                entity.Property(c => c.City).IsRequired().HasMaxLength(100);

                entity.Property(c => c.CustomerNumber).IsRequired();
                entity.Property(c => c.Notes).HasMaxLength(1000);

                entity.Property(c => c.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(c => c.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(c => new { c.TenantId, c.Email })
                    .IsUnique()
                    .HasDatabaseName("IX_Customer_TenantId_Email");

                entity.HasIndex(c => c.TenantId)
                    .HasDatabaseName("IX_Customer_TenantId");

                entity.HasIndex(c => new { c.TenantId, c.FirstName })
                    .HasDatabaseName("IX_Customer_TenantId_FirstName");

                entity.HasIndex(c => new { c.TenantId, c.LastName })
                    .HasDatabaseName("IX_Customer_TenantId_LastName");

                entity.HasIndex(c => new { c.TenantId, c.CustomerNumber })
                    .IsUnique()
                    .HasDatabaseName("IX_Customer_TenantId_CustomerNumber");

                entity.HasMany(c => c.Appointments)
                    .WithOne(a => a.Customer)
                    .HasForeignKey(a => a.CustomerId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ==================== APPOINTMENT ====================
            modelBuilder.Entity<Appointment>(entity =>
            {
                entity.HasKey(a => a.Id);

                entity.Property(a => a.Id).HasDefaultValueSql("NEWID()");
                entity.Property(a => a.TenantId).IsRequired();
                entity.Property(a => a.CustomerId).IsRequired();

                entity.Property(a => a.OrderNumber).IsRequired().HasMaxLength(50);
                entity.Property(a => a.AppointmentDate).IsRequired();
                entity.Property(a => a.Description).IsRequired().HasMaxLength(500);

                entity.Property(a => a.Status)
                    .IsRequired()
                    .HasConversion<int>()
                    .HasDefaultValue(AppointmentStatus.Scheduled);

                entity.Property(a => a.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(a => a.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(a => new { a.TenantId, a.AppointmentDate })
                    .HasDatabaseName("IX_Appointment_TenantId_AppointmentDate");

                entity.HasIndex(a => new { a.TenantId, a.CustomerId })
                    .HasDatabaseName("IX_Appointment_TenantId_CustomerId");

                entity.HasIndex(a => new { a.TenantId, a.Status })
                    .HasDatabaseName("IX_Appointment_TenantId_Status");

                //entity.HasIndex(a => a.OrderNumber)
                //    .HasDatabaseName("IX_Appointment_OrderNumber");
                entity.HasIndex(a => a.OrderNumber)
                .IsUnique()
                .HasDatabaseName("UX_Appointment_OrderNumber");
            });

            // ==================== BUSINESS ====================
            modelBuilder.Entity<Business>(entity =>
            {
                entity.HasKey(b => b.Id);

                entity.Property(b => b.Id).HasDefaultValueSql("NEWID()");
                entity.Property(b => b.Name).IsRequired().HasMaxLength(200);

                entity.Property(b => b.LogoUrl).HasMaxLength(500);
                entity.Property(b => b.PrimaryColor).HasMaxLength(10);
                entity.Property(b => b.SecondaryColor).HasMaxLength(10);

                entity.Property(b => b.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(b => b.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.Property(b => b.EnableServices).HasDefaultValue(true);
                entity.Property(b => b.EnableStaffing).HasDefaultValue(true);
                entity.Property(b => b.EnablePayments).HasDefaultValue(true);
                entity.Property(b => b.EnableInvoices).HasDefaultValue(true);
            });

            // ==================== SERVICE CATEGORY ====================
            modelBuilder.Entity<ServiceCategory>(entity =>
            {
                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id).HasDefaultValueSql("NEWID()");
                entity.Property(x => x.BusinessId).IsRequired();

                entity.Property(x => x.Name).IsRequired().HasMaxLength(120);

                entity.Property(x => x.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(x => x.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => new { x.BusinessId, x.Name })
                    .IsUnique()
                    .HasDatabaseName("IX_ServiceCategory_BusinessId_Name");

                entity.HasOne(x => x.Business)
                    .WithMany()
                    .HasForeignKey(x => x.BusinessId)
                    .OnDelete(DeleteBehavior.Cascade);

                // IMPORTANT: do NOT configure Services relationship here
                // (it creates duplicate relationships + shadow FKs like ServiceCategoryId1)
            });

            // ==================== SERVICE OFFERING ====================
            modelBuilder.Entity<ServiceOffering>(entity =>
            {
                entity.HasKey(s => s.Id);

                entity.Property(s => s.Id).HasDefaultValueSql("NEWID()");
                entity.Property(s => s.BusinessId).IsRequired();

                entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
                entity.Property(s => s.Price).HasColumnType("decimal(18,2)");
                entity.Property(s => s.IsActive).HasDefaultValue(true);

                entity.Property(s => s.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(s => s.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.HasOne(s => s.Business)
                    .WithMany(b => b.Services)
                    .HasForeignKey(s => s.BusinessId)
                    .OnDelete(DeleteBehavior.Cascade);

                
                entity.HasOne(s => s.Category)
                    .WithMany(c => c.Services)
                    .HasForeignKey(s => s.CategoryId)
                    .OnDelete(DeleteBehavior.NoAction);

                entity.HasIndex(s => new { s.BusinessId, s.Name })
                    .HasDatabaseName("IX_ServiceOffering_BusinessId_Name");
            });

            // ==================== APPOINTMENT SERVICES JOIN ====================
            modelBuilder.Entity<AppointmentServiceModel>(entity =>
            {
                entity.HasKey(x => new { x.AppointmentId, x.ServiceOfferingId });

                entity.Property(x => x.PriceAtBooking).HasColumnType("decimal(18,2)");

                entity.HasOne(x => x.Appointment)
                    .WithMany(a => a.AppointmentServices)
                    .HasForeignKey(x => x.AppointmentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(x => x.ServiceOffering)
                    .WithMany(s => s.AppointmentServices)
                    .HasForeignKey(x => x.ServiceOfferingId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // ==================== IDENTITY USER ====================
            modelBuilder.Entity<AppointMeAppUser>(entity =>
            {
                entity.Property(u => u.TenantId); // nullable

                entity.Property(u => u.FirstName).HasMaxLength(50);
                entity.Property(u => u.LastName).HasMaxLength(50);

                entity.Property(u => u.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(u => u.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(u => u.TenantId)
                    .HasDatabaseName("IX_AppointMeAppUser_TenantId");
            });

            // ==================== INVOICE ====================
            modelBuilder.Entity<Invoice>(entity =>
            {
                entity.HasKey(i => i.Id);

                entity.Property(i => i.Id).HasDefaultValueSql("NEWID()");
                entity.Property(i => i.TenantId).IsRequired();
                entity.Property(i => i.AppointmentId).IsRequired();
                entity.Property(i => i.CustomerId).IsRequired();

                entity.Property(i => i.InvoiceNumber).IsRequired().HasMaxLength(50);

                entity.Property(i => i.CustomerNameSnapshot).IsRequired().HasMaxLength(120);
                entity.Property(i => i.CustomerEmailSnapshot).HasMaxLength(256);

                entity.Property(i => i.BusinessNameSnapshot).IsRequired().HasMaxLength(200);
                entity.Property(i => i.BusinessAddressSnapshot).HasMaxLength(500);
                entity.Property(i => i.AppointmentOrderNumberSnapshot).HasMaxLength(50);

                entity.Property(i => i.Subtotal).HasColumnType("decimal(18,2)");
                entity.Property(i => i.Discount).HasColumnType("decimal(18,2)");
                entity.Property(i => i.Tax).HasColumnType("decimal(18,2)");
                entity.Property(i => i.Total).HasColumnType("decimal(18,2)");

                entity.Property(i => i.Status)
                    .HasConversion<int>()
                    .HasDefaultValue(InvoiceStatus.Draft);

                entity.Property(i => i.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(i => i.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");

                // One invoice per appointment per tenant
                entity.HasIndex(i => new { i.TenantId, i.AppointmentId })
                    .IsUnique()
                    .HasDatabaseName("IX_Invoice_TenantId_AppointmentId");

                // Invoice number unique per tenant
                entity.HasIndex(i => new { i.TenantId, i.InvoiceNumber })
                    .IsUnique()
                    .HasDatabaseName("IX_Invoice_TenantId_InvoiceNumber");

                entity.HasMany(i => i.Lines)
                    .WithOne(l => l.Invoice)
                    .HasForeignKey(l => l.InvoiceId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ==================== INVOICE LINE ====================
            modelBuilder.Entity<InvoiceLine>(entity =>
            {
                entity.HasKey(l => l.Id);
                entity.Property(l => l.Id).HasDefaultValueSql("NEWID()");

                entity.Property(l => l.NameSnapshot).IsRequired().HasMaxLength(200);
                entity.Property(l => l.CategorySnapshot).HasMaxLength(120);

                entity.Property(l => l.UnitPrice).HasColumnType("decimal(18,2)");
                entity.Property(l => l.LineTotal).HasColumnType("decimal(18,2)");
            });
        }
    }
}
