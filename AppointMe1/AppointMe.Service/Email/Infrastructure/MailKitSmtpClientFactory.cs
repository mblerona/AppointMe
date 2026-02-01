namespace AppointMe.Service.Email.Infrastructure;

public sealed class MailKitSmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClientAdapter Create() => new MailKitSmtpClientAdapter();
}
