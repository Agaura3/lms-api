using System.Net;
using System.Text.Json;
using FluentValidation;
using lms_api.Common;

namespace lms_api.Middleware;

public class ValidationExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ValidationExceptionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";

            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            var response = ApiResponse<object>.FailResponse(
                string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)),
                context.TraceIdentifier);

            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                response.Success,
                response.Message,
                response.TraceId,
                errors
            }));
        }
    }
}
