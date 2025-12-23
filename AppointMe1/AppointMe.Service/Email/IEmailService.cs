using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Email
{
    public interface IEmailService
    {
        Task SendEmailAsync(EmailMessage message);
        Task SendEmailWithAttachmentAsync(
          EmailMessage message,
          byte[] attachmentBytes,
          string attachmentFileName,
          string contentType
      );
    }
}
