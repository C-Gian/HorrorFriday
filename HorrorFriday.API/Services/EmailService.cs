using System.Net;
using System.Net.Mail;

namespace HorrorFriday.API.Services;

public interface IEmailService
{
    Task SendPasswordResetAsync(string toEmail, string resetLink);
}

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;

    public SmtpEmailService(IConfiguration config) => _config = config;

    public async Task SendPasswordResetAsync(string toEmail, string resetLink)
    {
        var host = _config["Smtp:Host"]!;
        var port = int.Parse(_config["Smtp:Port"]!);
        var username = _config["Smtp:Username"]!;
        var password = _config["Smtp:Password"]!;
        var from = _config["Smtp:From"]!;

        using var client = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(username, password),
            EnableSsl = true
        };

        var body = $"""
            <html>
            <body style="font-family:sans-serif;background:#0e0d0c;color:#e8e3de;padding:40px 0;margin:0">
              <div style="max-width:480px;margin:0 auto;padding:40px 32px;background:#161412;border-radius:12px;border:1px solid #2a2220">
                <h2 style="color:#e8e3de;margin:0 0 8px">Reset password</h2>
                <p style="color:#a09890;margin:0 0 32px;font-size:14px">Hai richiesto il reset della password per il tuo account HorrorFriday. Clicca il pulsante qui sotto per impostarne una nuova.</p>
                <a href="{resetLink}" style="display:inline-block;background:#9b1c1c;color:#fff;padding:13px 28px;border-radius:8px;text-decoration:none;font-weight:600;font-size:15px">Reimposta password</a>
                <p style="color:#6b5c55;font-size:12px;margin:32px 0 0">Il link scade tra 1 ora. Se non hai richiesto il reset, ignora questa email.</p>
              </div>
            </body>
            </html>
            """;

        using var msg = new MailMessage
        {
            From = new MailAddress(from, "HorrorFriday"),
            Subject = "Reimposta la tua password HorrorFriday",
            Body = body,
            IsBodyHtml = true
        };
        msg.To.Add(toEmail);
        await client.SendMailAsync(msg);
    }
}
