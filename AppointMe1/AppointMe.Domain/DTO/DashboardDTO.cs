namespace AppointMe.Domain.DTO
{
    public class DashboardDTO
    {
        public string BusinessName { get; set; } = "";
        public string? LogoUrl { get; set; }

        public int TotalAppointments { get; set; }
        public int ScheduledAppointments { get; set; }
        public int CompletedAppointments { get; set; }
        public int CancelledAppointments { get; set; }
        public int CustomersCount { get; set; }

        public List<DashboardAppointmentRowDTO> Rows { get; set; } = new();
    }

    public class DashboardAppointmentRowDTO
    {
        public Guid Id { get; set; }
        public string CustomerName { get; set; } = "";
        public string? Description { get; set; }
        public string OrderNumber { get; set; } = "";
        public DateTime AppointmentDate { get; set; }
        public string Email { get; set; } = "";
        public string Phone1 { get; set; } = "";
        public string? Phone2 { get; set; }
        public string Location { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
