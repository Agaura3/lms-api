using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace LMS.API.Services
{
    public class ResendEmailService : IEmailService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public ResendEmailService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;
        }

        public async Task SendEmailAsync(string email, string subject, string html)
        {
            var apiKey = _config["Resend:ApiKey"];

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var body = new
            {
                from = "onboarding@resend.dev",
                to = new[] { email },
                subject = subject,
                html = html
            };

            var content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            );

            await _httpClient.PostAsync("https://api.resend.com/emails", content);
        }
    }
}