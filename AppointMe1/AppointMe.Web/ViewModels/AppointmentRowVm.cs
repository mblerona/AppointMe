namespace AppointMe.Web.ViewModels
{
    public class AppointmentRowVm
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
