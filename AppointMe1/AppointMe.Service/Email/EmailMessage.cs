using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Email
{
    public class EmailMessage
    {
        public string MailTo { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Content { get; set; } = "";

        /// <summary>True when <see cref="Content"/> is HTML (e.g. Identity emails); plain text otherwise.</summary>
        public bool IsHtml { get; set; }
    }
}
