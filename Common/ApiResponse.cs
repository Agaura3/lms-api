namespace lms_api.Common;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
    public string? TraceId { get; set; }

    public static ApiResponse<T> SuccessResponse(T data, string message = "Success", string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Message = message,
            Data = data,
            TraceId = traceId
        };
    }

    public static ApiResponse<T> FailResponse(string message, string? traceId = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            TraceId = traceId
        };
    }
}
