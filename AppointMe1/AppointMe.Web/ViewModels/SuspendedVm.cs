using AppointMe.Domain.DTO;

namespace AppointMe.Web.ViewModels
{
    public class SuspendedVm
    {
        public SuspensionInfoDTO Suspension { get; set; } = new();
        public string? SupportEmail { get; set; }
    }
}
