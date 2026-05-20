using System.Net;
using System.Text.Json;
using lms_api.Common;

namespace lms_api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var traceId = context.TraceIdentifier;
        _logger.LogError(ex, "Unhandled exception. TraceId={TraceId}", traceId);

        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/json";

        var message = _env.IsDevelopment()
            ? ex.Message
            : "An unexpected error occurred. Please try again later.";

        var response = ApiResponse<string>.FailResponse(message, traceId);
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
