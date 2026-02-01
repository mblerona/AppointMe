namespace AppointMe.Service.Email.Infrastructure;

public interface ISmtpClientFactory
{
    ISmtpClientAdapter Create();
}
