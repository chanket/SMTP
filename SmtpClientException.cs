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
    public class SmtpClientException : Exception
    {
        public SmtpClientException(string message) : base(message)
        {
        }
    }
}
