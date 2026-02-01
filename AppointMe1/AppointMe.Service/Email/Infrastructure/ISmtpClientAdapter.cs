using MailKit.Security;
using MimeKit;

namespace AppointMe.Service.Email.Infrastructure;

public interface ISmtpClientAdapter : IDisposable
{
    bool CheckCertificateRevocation { get; set; }

    Task ConnectAsync(string host, int port, SecureSocketOptions options);
    Task AuthenticateAsync(string userName, string password);
    Task SendAsync(MimeMessage message);
    Task DisconnectAsync(bool quit);
}
