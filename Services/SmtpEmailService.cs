using System.Net;
using System.Net.Mail;

namespace lms_api.Services;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;

    public SmtpEmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        var smtpSection = _config.GetSection("EmailSettings");

        var client = new SmtpClient(smtpSection["SmtpHost"])
        {
            Port = int.Parse(smtpSection["SmtpPort"]!),
            Credentials = new NetworkCredential(
                smtpSection["SmtpUser"],
                smtpSection["SmtpPassword"]),
            EnableSsl = bool.Parse(smtpSection["EnableSsl"]!)
        };

        var fromEmail = smtpSection["SmtpUser"] 
    ?? throw new Exception("SMTP user not configured.");

    var mail = new MailMessage(
    fromEmail,
    toEmail,
    subject,
    body);

        await client.SendMailAsync(mail);
    }
}