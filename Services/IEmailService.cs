
using LMS.API.Services;
namespace LMS.API.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string html);
    }
}