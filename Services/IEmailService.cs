namespace lms_api.Services;

public interface IEmailService
{
    Task SendEmailAsync(string toEmail, string subject, string body);
}