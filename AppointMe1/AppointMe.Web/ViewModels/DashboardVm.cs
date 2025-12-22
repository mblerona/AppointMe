namespace AppointMe.Web.ViewModels
{
    public class DashboardVm
    {
        public string BusinessName { get; set; } = "";
        public string? LogoUrl { get; set; }

        public int TotalAppointments { get; set; }
        public int ScheduledAppointments { get; set; }
        public int ReschedulesAppointments { get; set; }
        public int CompletedAppointments { get; set; }
        public int CancelledAppointments { get; set; }
        public int CustomersCount { get; set; }

      
        public string Range { get; set; } = "all";      
        public string? Search { get; set; }
        public string Status { get; set; } = "all";    

        // Table
        public List<AppointmentRowVm> Rows { get; set; } = new();
    }
}
