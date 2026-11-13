namespace AppointMe.Domain.DomainModels
{
    /// <summary>
    /// Which optional features are in effect. The stored switches are the owner's choices;
    /// "invoices require services" is applied here, when reading, and never written back,
    /// so turning services off and on again restores the previous invoicing choice.
    /// </summary>
    public static class BusinessFeatures
    {
        public static bool ServicesEnabled(this Business business) => business.EnableServices;

        /// <summary>Invoices are built from an appointment's services, so they need services on.</summary>
        public static bool InvoicesEnabled(this Business business) =>
            business.EnableServices && business.EnableInvoices;
    }
}
