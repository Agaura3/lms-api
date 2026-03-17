using Mailjet.Client;
using Mailjet.Client.Resources;
using Newtonsoft.Json.Linq;
using lms_api.Services;

public class MailjetEmailService : IEmailService
{
    private readonly IConfiguration _config;

    public MailjetEmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body)
    {
        var apiKey = _config["EmailSettings:ApiKey"];
        var secretKey = _config["EmailSettings:SecretKey"];

        var client = new MailjetClient(apiKey, secretKey);

       var request = new MailjetRequest
{
    Resource = Send.Resource,
}
.Property(Send.FromEmail, _config["EmailSettings:FromEmail"])
.Property(Send.FromName, "LMS")
.Property(Send.Subject, subject)
.Property(Send.TextPart, body)
.Property(Send.HtmlPart, $"<h3>{body}</h3>")
.Property(Send.Recipients, new JArray {
    new JObject {
        {"Email", toEmail}
    }
});

var response = await client.PostAsync(request);  // 🔥 yahi

Console.WriteLine("Mailjet Status: " + response.StatusCode);

if (!response.IsSuccessStatusCode)
{
    Console.WriteLine("Mailjet Error: " + response.GetErrorMessage());
    throw new Exception("Mailjet error: " + response.GetErrorMessage());
}
}
}