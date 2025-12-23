using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Email
{
    public class MailSettings
    {
        public string SmtpServer { get; set; } = "";
        public int SmtpServerPort { get; set; }
        public bool EnableSsl { get; set; }

        public string SenderName { get; set; } = "";
        public string EmailDisplayName { get; set; } = "";

        public string SmtpUserName { get; set; } = "";
        public string SmtpPassword { get; set; } = "";
    }
}
