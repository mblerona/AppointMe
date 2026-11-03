namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>
/// Program.cs requires a DefaultConnection at startup. The "Testing" environment does not load
/// appsettings.Development.json, so the factories supply this placeholder. It is never opened:
/// every factory replaces the SQL Server DbContext with an in-memory database.
/// </summary>
public static class TestConnection
{
    public const string Placeholder = "Server=unused-in-tests;Database=unused-in-tests";
}
