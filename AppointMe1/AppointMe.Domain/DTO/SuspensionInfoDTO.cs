namespace AppointMe.Domain.DTO
{
    /// <summary>What a suspended tenant sees about their own suspension.</summary>
    public class SuspensionInfoDTO
    {
        public string BusinessName { get; set; } = "";
        public string Reason { get; set; } = "";
        public string? Note { get; set; }

        /// <summary>In the business's time zone.</summary>
        public DateTime? SuspendedAt { get; set; }
    }
}
