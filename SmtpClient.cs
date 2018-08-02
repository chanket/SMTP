using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Licc.Smtp
{
    /// <summary>
    /// 基于SMTP协议实现邮件发送功能的客户端类。
    /// </summary>
    public class SmtpClient
    {
        #region 私有域
        /// <summary>
        /// SMTP服务器主机。
        /// </summary>
        public string Host { get; protected set; }
        /// <summary>
        /// SMTP服务器端口。
        /// </summary>
        public int Port { get; protected set; }
        /// <summary>
        /// 指明连接是否使用SSL加密。
        /// </summary>
        public bool Ssl { get; protected set; }
        #endregion

        #region 公共方法
        /// <summary>
        /// 通过指定的SMTP服务器，创建新的SmtpClient实例。可使用host:port格式。
        /// </summary>
        /// <param name="address">SMTP服务器地址。未指定端口时为25（不使用SSL）或465（使用SSL）。</param>
        /// <param name="ssl">指明连接是否使用SSL加密。</param>
        /// <exception cref="ArgumentException">格式错误</exception>
        public SmtpClient(string address, bool ssl = false)
        {
            //解析address，获取主机host和端口port
            Regex regHost = new Regex(@"^([^\:]+)\:?(\d*)$", RegexOptions.Compiled);
            Match matchHost = regHost.Match(address);
            if (!matchHost.Success)
            {
                throw new ArgumentException("无法解析的服务器地址");
            }
            this.Ssl = ssl;
            this.Host = matchHost.Groups[1].Value;
            string port = matchHost.Groups[2].Value;
            if (string.IsNullOrEmpty(port))
            {
                if (ssl) this.Port = 465;
                else this.Port = 25;
            }
            else
            {
                int outPort;
                if (!int.TryParse(port, out outPort))
                {
                    throw new ArgumentException("无法解析端口号");
                }
                else
                {
                    Port = outPort;
                }
                if (this.Port <= 0 || this.Port >= 65536)
                {
                    throw new ArgumentException("错误的端口号");
                }
            }
        }

        /// <summary>
        /// 发送邮件。这个重载具备最简单的功能，只能对单收件人发送文本内容。
        /// </summary>
        /// <param name="username">发件人邮箱/用户名</param>
        /// <param name="password">发件人密码</param>
        /// <param name="to">收件人邮箱</param>
        /// <param name="subject">标题</param>
        /// <param name="content">文本内容</param>
        /// <exception cref="SmtpClientAuthException">用户名/密码错误</exception>
        /// <exception cref="SmtpClientException">交互异常</exception>
        /// <exception cref="Exception">其它异常</exception>
        public async Task SendAsync(string username, string password, string to, string subject, string content)
        {
            await SendAsync(username, username, password, new string[] { to }, subject, content, false, null).ConfigureAwait(false);
        }

        /// <summary>
        /// 发送邮件。这个重载具备最完整的功能，可以指定多个收件人、附件等。
        /// </summary>
        /// <param name="friendlyName">发件人名称</param>
        /// <param name="username">发件人邮箱/用户名</param>
        /// <param name="password">发件人密码</param>
        /// <param name="to">收件人邮箱列表</param>
        /// <param name="subject">标题</param>
        /// <param name="content">内容</param>
        /// <param name="isContentHTML">指示内容是否为HTML</param>
        /// <param name="files">附件列表</param>
        /// <exception cref="SmtpClientAuthException">用户名/密码错误</exception>
        /// <exception cref="SmtpClientException">交互异常</exception>
        /// <exception cref="Exception">其它异常</exception>
        public async Task SendAsync(string friendlyName, string username, string password, IEnumerable<string> to, string subject, string content, bool isContentHTML, IEnumerable<SmtpClientAttach> files)
        {
            using (var stream = await ConnectAsync(this.Host, this.Port, this.Ssl).ConfigureAwait(false))
            {
                await HandshakeAsync(stream).ConfigureAwait(false);
                await LoginAsync(stream, username, password).ConfigureAwait(false);
                await SendAsync(stream, friendlyName, username, to, subject, content, isContentHTML, files).ConfigureAwait(false);
            }
        }
        #endregion

        #region 私有方法

        /// <summary>
        /// 对字符串以指定的字符集进行Base64编码。
        /// </summary>
        /// <param name="data">需要编码的字符串。</param>
        /// <param name="e">指定的字符集，默认为UTF8。</param>
        /// <returns>编码后的字符串。</returns>
        private string Base64Encode(string data, Encoding e = null)
        {
            if (data == null) return "";

            if (e == null) e = Encoding.UTF8;
            byte[] buffer = e.GetBytes(data);
            return Convert.ToBase64String(buffer);
        }

        /// <summary>
        /// 对Base64编码后的字符串以指定的字符集进行解码。
        /// </summary>
        /// <param name="base64">需要解码的字符串。</param>
        /// <param name="e">指定的字符集，默认为UTF8。</param>
        /// <returns>解码后的字符串。</returns>
        private string Base64Decode(string base64, Encoding e = null)
        {
            if (base64 == null) return "";

            if (e == null) e = Encoding.UTF8;
            byte[] buffer = Convert.FromBase64String(base64);
            return e.GetString(buffer);
        }

        /// <summary>
        /// 对字符串以指定的字符集进行基于Base64的扩展首部编码。
        /// </summary>
        /// <param name="data">需要编码的字符串。</param>
        /// <param name="e">指定的字符集，默认为UTF8。</param>
        /// <returns>编码后的字符串。</returns>
        private string Base64ExtendedWordEncode(string data, Encoding e = null)
        {
            if (data == null) return "";

            if (e == null) e = Encoding.UTF8;
            return "=?" + e.HeaderName.ToUpper() + "?B?" + Base64Encode(data, e) + "?=";
        }

        /// <summary>
        /// 将收件人数组rcpt[]，转换为To首部能够接受的表示。
        /// </summary>
        /// <returns>转化后的结果。</returns>
        private string RcptMerge(string[] to)
        {
            string retval = "";
            if (to == null) return retval;

            int index;
            for (index = 0; index < to.Length - 1; index++)
            {
                retval += "<" + to[index] + ">, ";
            }
            retval += "<" + to[index] + ">";
            return retval;
        }

        /// <summary>
        /// 连接指定的SMTP服务器。
        /// </summary>
        /// <param name="host">SMTP主机名</param>
        /// <param name="port">SMTP端口</param>
        /// <param name="useSsl">使用SSL</param>
        /// <exception cref="SmtpClientException">交互异常</exception>
        /// <exception cref="Exception">其它异常</exception>
        /// <returns>套接字IO流</returns>
        private async Task<Stream> ConnectAsync(string host, int port, bool useSsl)
        {
            //建立TCP连接
            var client = new TcpClient();
            await client.ConnectAsync(host, port).ConfigureAwait(false);

            //根据是否使用SSL，建立TCP输入输出流
            if (useSsl)
            {
                SslStream ssl = new SslStream(client.GetStream());
                try
                {
                    await ssl.AuthenticateAsClientAsync(host).ConfigureAwait(false);
                }
                catch
                {
                    //SSL认证失败
                    throw new SmtpClientException("无法完成SSL认证。");
                }
                return ssl;
            }
            else
            {
                return client.GetStream();
            }
        }

        /// <summary>
        /// （在连接后）完成SMTP握手交互。
        /// </summary>
        /// <param name="stream">套接字IO流</param>
        /// <exception cref="SmtpClientException">交互异常</exception>
        /// <exception cref="Exception">其它异常</exception>
        /// <returns></returns>
        private async Task HandshakeAsync(Stream stream)
        {
            //握手
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
            using (var writer = new StreamWriter(stream, Encoding.ASCII, 4096, true))
            {
                writer.AutoFlush = true;

                //完成SMTP握手
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (!line.StartsWith("220 "))
                {
                    //无法完成SMTP握手
                    throw new SmtpClientException("无法完成SMTP握手220。");
                }

                writer.WriteLine("EHLO " + this.Host);
                line = await reader.ReadLineAsync().ConfigureAwait(false);
                while (line.StartsWith("250-"))
                {
                    //TODO: 分析服务器支持的扩充功能
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }

                if (!line.StartsWith("250 "))
                {
                    //无法完成SMTP握手
                    throw new SmtpClientException("无法完成SMTP握手250。");
                }
            }
        }

        /// <summary>
        /// （在握手后）完成SMTP登陆。
        /// </summary>
        /// <param name="stream">套接字IO流</param>
        /// <param name="username">用户名</param>
        /// <param name="password">密码</param>
        /// <exception cref="SmtpClientAuthException">用户名/密码错误</exception>
        /// <exception cref="SmtpClientException">交互异常</exception>
        /// <exception cref="Exception">其它异常</exception>
        /// <returns></returns>
        private async Task LoginAsync(Stream stream, string username, string password)
        {
            //登录
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
            using (var writer = new StreamWriter(stream, Encoding.ASCII, 4096, true))
            {
                writer.AutoFlush = true;

                //发起登录请求
                await writer.WriteLineAsync("AUTH LOGIN").ConfigureAwait(false);

                //发送用户名
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (!line.StartsWith("334 "))
                {
                    throw new SmtpClientException("登录时收到无法识别的响应。预期：334。");
                }
                line = line.Substring(4);
                line = Base64Decode(line);
                if (line.ToLower() != "username:")
                {
                    throw new SmtpClientException("登录时收到无法识别的响应。预期：username。");
                }
                await writer.WriteLineAsync(Base64Encode(username)).ConfigureAwait(false);

                //发送密码
                line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (!line.StartsWith("334 "))
                {
                    throw new SmtpClientException("登录时收到无法识别的响应。预期：334。");
                }
                line = line.Substring(4);
                line = Base64Decode(line);
                if (line.ToLower() != "password:")
                {
                    throw new SmtpClientException("登录时收到无法识别的响应。预期：password。");
                }
                await writer.WriteLineAsync(Base64Encode(password)).ConfigureAwait(false);

                //分析登录结果
                line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line.StartsWith("5"))
                {
                    throw new SmtpClientException("无法登陆。");
                }
                else if (!line.StartsWith("235 "))
                {
                    throw new SmtpClientException("登录用户名或密码错误。");
                }
            }
        }

        /// <summary>
        /// （在登陆后）发送邮件。
        /// </summary>
        /// <param name="stream">套接字IO流</param>
        /// <param name="name">发件人名称</param>
        /// <param name="from">发件人邮箱</param>
        /// <param name="to">收件人邮箱列表</param>
        /// <param name="subject">标题</param>
        /// <param name="content">正文</param>
        /// <param name="isContentHTML">正文是否为HTML</param>
        /// <param name="files">附件列表</param>
        /// <exception cref="SmtpClientException">交互异常</exception>
        /// <exception cref="Exception">其它异常</exception>
        /// <returns></returns>
        private async Task SendAsync(Stream stream, string name, string from, IEnumerable<string> to, string subject, string content, bool isContentHTML, IEnumerable<SmtpClientAttach> files)
        {
            //发送
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
            using (var writer = new StreamWriter(stream, Encoding.ASCII, 4096, true))
            {
                writer.AutoFlush = true;

                //发送人
                await writer.WriteLineAsync("MAIL FROM: <" + from + ">").ConfigureAwait(false);
                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (!line.StartsWith("250 "))
                {
                    throw new SmtpClientException("发送时收到未预期的响应：MAIL FROM。");
                }

                //收件人
                foreach (string r in to)
                {
                    await writer.WriteLineAsync("RCPT TO: <" + r + ">").ConfigureAwait(false);
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (!line.StartsWith("250 "))
                    {
                        throw new SmtpClientException("发送时收到未预期的响应：RCPT TO。");
                    }
                }

                //准备正文
                await writer.WriteLineAsync("DATA").ConfigureAwait(false);
                line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (!line.StartsWith("354 "))
                {
                    throw new SmtpClientException("发送时收到未预期的响应：DATA。");
                }

                //MIME报文
                string boundary = "=====Licc_NextPart" + DateTime.Now.Ticks + "=====";
                string send = "";
                send += "From: \"" + Base64ExtendedWordEncode(name) + "\" <" + from + ">" + Environment.NewLine;
                send += "To: " + RcptMerge(to.ToArray()) + Environment.NewLine;
                send += "Subject: " + Base64ExtendedWordEncode(subject) + Environment.NewLine;
                send += "Mime-Version: 1.0" + Environment.NewLine;
                send += "Content-Type: multipart/mixed;" + Environment.NewLine;
                send += "\tboundary=\"" + boundary + "\"" + Environment.NewLine;
                send += "Content-Transfer-Encoding: 7bit" + Environment.NewLine;
                send += Environment.NewLine;
                send += "This is a multi-part message in MIME format." + Environment.NewLine;
                send += Environment.NewLine + "--" + boundary + Environment.NewLine;

                //正文
                if (isContentHTML)
                {
                    send += "Content-Type: text/html; charset=\"utf-8\"" + Environment.NewLine;
                    send += "Content-Transfer-Encoding: base64" + Environment.NewLine;
                    send += Environment.NewLine;
                }
                else
                {
                    send += "Content-Type: text/plain; charset=\"utf-8\"" + Environment.NewLine;
                    send += "Content-Transfer-Encoding: base64" + Environment.NewLine;
                    send += Environment.NewLine;
                }
                send += Base64Encode(content) + Environment.NewLine;
                send += Environment.NewLine + "--" + boundary + Environment.NewLine;
                await writer.WriteAsync(send).ConfigureAwait(false);

                //附件
                if (files != null && files.Count() > 0)
                {
                    int index = 0;
                    byte[] buffer = new byte[1024 * 40 * 3];     //3的整数倍，这样Base64编码后才不会出现=
                    foreach (var file in files)
                    {
                        send = "";
                        send += "Content-Type: application/octet-stream; name=\"" + Base64ExtendedWordEncode(file.Name) + "\"" + Environment.NewLine;
                        send += "Content-Transfer-Encoding: base64" + Environment.NewLine;
                        send += "Content-Disposition: attachment; filename=\"" + Base64ExtendedWordEncode(file.Name) + "\"" + Environment.NewLine; send += Environment.NewLine;
                        await writer.WriteAsync(send).ConfigureAwait(false);

                        index++;
                        file.Stream.Position = 0;
                        int read = await file.Stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        while (read > 0)
                        {
                            //InvokeEvent(OnFileProgress, "正在上传第 " + index + " 个附件 " + (file.Stream.Position * 100 / file.Stream.Length) + "%");
                            string base64 = Convert.ToBase64String(buffer, 0, read);
                            await writer.WriteAsync(Convert.ToBase64String(buffer, 0, read)).ConfigureAwait(false);
                            read = await file.Stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                        }
                        //InvokeEvent(OnFileProgress, "正在上传第 " + index + " 个附件 " + (file.Stream.Position * 100 / file.Stream.Length) + "%");

                        send = "";
                        send += Environment.NewLine;
                        send += Environment.NewLine + "--" + boundary + Environment.NewLine;
                        await writer.WriteAsync(send).ConfigureAwait(false);
                    }
                }

                //发送
                await writer.WriteAsync("\r\n.\r\n").ConfigureAwait(false);
                line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (!line.StartsWith("250 "))
                {
                    throw new SmtpClientException("发送失败。");
                }
            }
        }
        #endregion
    }
}
