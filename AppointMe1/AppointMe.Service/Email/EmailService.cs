using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AppointMe.Service.Email
{
    public class EmailService : IEmailService
    {
        private readonly MailSettings _settings;

        public EmailService(IOptions<MailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task SendEmailAsync(EmailMessage message)
        {
            var email = BuildBaseMessage(message);

            email.Body = new TextPart(MimeKit.Text.TextFormat.Plain)
            {
                Text = message.Content
            };

            await SendAsync(email);
        }

        public async Task SendEmailWithAttachmentAsync(
            EmailMessage message,
            byte[] attachmentBytes,
            string attachmentFileName,
            string contentType)
        {
            var email = BuildBaseMessage(message);

            var builder = new BodyBuilder
            {
                TextBody = message.Content
            };

            builder.Attachments.Add(attachmentFileName, attachmentBytes, ContentType.Parse(contentType));

            email.Body = builder.ToMessageBody();

            await SendAsync(email);
        }

        private MimeMessage BuildBaseMessage(EmailMessage message)
        {
            var email = new MimeMessage();

            email.From.Add(new MailboxAddress(_settings.EmailDisplayName, _settings.SmtpUserName));
            email.To.Add(MailboxAddress.Parse(message.MailTo));
            email.Subject = message.Subject;

            return email;
        }

        private async Task SendAsync(MimeMessage email)
        {
            using var smtp = new SmtpClient();

            // Dev-friendly - avoids some revocation check issues on Windows
            smtp.CheckCertificateRevocation = false;

            var secureOption = _settings.EnableSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await smtp.ConnectAsync(_settings.SmtpServer, _settings.SmtpServerPort, secureOption);
            await smtp.AuthenticateAsync(_settings.SmtpUserName, _settings.SmtpPassword);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}




//using MailKit.Security;
//using Microsoft.Extensions.Options;
//using MimeKit;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using MailKit.Net.Smtp;
//using MailKit.Security;
//using Microsoft.Extensions.Options;
//using MimeKit;

//namespace AppointMe.Service.Email
//{
//    public class EmailService : IEmailService
//    {
//        private readonly MailSettings _settings;

//        public EmailService(IOptions<MailSettings> settings)
//        {
//            _settings = settings.Value;
//        }

//        public async Task SendEmailAsync(EmailMessage message)
//        {
//            var email = new MimeMessage();

//            // FROM
//            email.From.Add(new MailboxAddress(
//                _settings.EmailDisplayName,
//                _settings.SmtpUserName
//            ));

//            // TO
//            email.To.Add(MailboxAddress.Parse(message.MailTo));

//            // SUBJECT
//            email.Subject = message.Subject;

//            // BODY (plain text for now)
//            email.Body = new TextPart(MimeKit.Text.TextFormat.Plain)
//            {
//                Text = message.Content
//            };

//            using var smtp = new SmtpClient();

//            var secureOption = _settings.EnableSsl
//                ? SecureSocketOptions.StartTls
//                : SecureSocketOptions.None;

//            await smtp.ConnectAsync(
//                _settings.SmtpServer,
//                _settings.SmtpServerPort,
//                secureOption
//            );

//            await smtp.AuthenticateAsync(
//                _settings.SmtpUserName,
//                _settings.SmtpPassword
//            );

//            await smtp.SendAsync(email);
//            await smtp.DisconnectAsync(true);
//        }

//        private MimeMessage BuildBaseMessage(EmailMessage message)
//        {
//            var email = new MimeMessage();

//            // FROM (system sender)
//            email.From.Add(new MailboxAddress(_settings.EmailDisplayName, _settings.SmtpUserName));

//            // TO
//            email.To.Add(MailboxAddress.Parse(message.MailTo));

//            // SUBJECT
//            email.Subject = message.Subject;

//            return email;
//        }

//        private async Task SendAsync(MimeMessage email)
//        {
//            using var smtp = new SmtpClient();


//            smtp.CheckCertificateRevocation = false;

//            var secureOption = _settings.EnableSsl
//                ? SecureSocketOptions.StartTls
//                : SecureSocketOptions.None;

//            await smtp.ConnectAsync(_settings.SmtpServer, _settings.SmtpServerPort, secureOption);

//            await smtp.AuthenticateAsync(_settings.SmtpUserName, _settings.SmtpPassword);

//            await smtp.SendAsync(email);
//            await smtp.DisconnectAsync(true);
//        }

//        public  async Task SendEmailWithAttachmentAsync(EmailMessage message, byte[] attachmentBytes, string attachmentFileName, string contentType)
//        {
//            var email = BuildBaseMessage(message);

//            var builder = new BodyBuilder
//            {
//                TextBody = message.Content
//            };

//            // Attachment
//            builder.Attachments.Add(attachmentFileName, attachmentBytes, ContentType.Parse(contentType));

//            email.Body = builder.ToMessageBody();

//            await SendAsync(email);
//        }

//    }
//}
