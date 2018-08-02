using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Licc.Smtp
{
    /// <summary>
    /// SmtpClient类抛出的异常。
    /// </summary>
    public class SmtpClientAuthException : SmtpClientException
    {
        public SmtpClientAuthException(string message) : base(message)
        {
        }
    }
}
