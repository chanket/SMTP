# SMTP
简易的异步接口的SMTP客户端。支持SSL、HTML、附件。<br />
A simple SMTP client with async interfaces. SSL, HTML and attach files are supported. <br />

# Usage
    string server = "smtp.domain.com";
    string username = "sender@domain.com";
    string password = "senderPassword";
    string[] to = new string[] {
        "receiver1@domain.com",
        "receiver2@domain.com",
        //More receivers
    };
    string subject = "This is a test.";
    string content = "<p>Hello World! 你好，世界！</p>";
    SmtpClientAttach[] files = new SmtpClientAttach[] {
        new SmtpClientAttach() { Name="Attach1.txt", Stream = new MemoryStream(Encoding.ASCII.GetBytes("Content of Attach File 1")) },
        new SmtpClientAttach() { Name="Attach2.txt", Stream = new MemoryStream(Encoding.ASCII.GetBytes("Content of Attach File 2")) },
        //More attach files
        //You can just use FileStream instead of MemoryStream to specify an attach file
    };

    SmtpClient sc = new SmtpClient(server, true); //or false if you don't want to use SSL
    await sc.SendAsync(username, password, to, subject, content, true, files);

