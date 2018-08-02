using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Licc.Smtp
{
    /// <summary>
    /// 用于描述SMTP待发送的附件。
    /// </summary>
    public class SmtpClientAttach
    {
        /// <summary>
        /// 附件的名称。
        /// </summary>
        public string Name;
        /// <summary>
        /// 附件的流。
        /// </summary>
        public Stream Stream;
    }
}
