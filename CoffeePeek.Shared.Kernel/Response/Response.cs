using System.Text.Json.Serialization;

namespace CoffeePeek.Shared.Kernel.Response;

public class Response
{
    public bool IsSuccess { get; init; }
    
    public string Message { get; init; } = string.Empty;
    
    public object? Data { get; init; }

    /// <summary>HTTP status when the error maps to a status code; null for non-HTTP failures.</summary>
    public int? StatusCode { get; init; }

    [JsonConstructor]
    public Response()
    {
        Message = string.Empty;
    }

    public Response(bool isSuccess, string message, object? data)
    {
        IsSuccess = isSuccess;
        Message = message;
        Data = data;
    }
    
    /// <summary>
    /// Creates a successful response with data.
    /// </summary>
    public static Response Success(object? data = null, string? message = null)
    {
        return new Response
        {
            IsSuccess = true,
            Message = message ?? "Operation successful",
            Data = data,
        };
    }
    
    /// <summary>
    /// Creates an error response with message and optional validation errors.
    /// </summary>
    public static Response Error(int statusCode, string message)
    {
        return new Response
        {
            IsSuccess = false,
            Message = message,
            Data = null,
            StatusCode = statusCode,
        };
    }
    
    /// <summary>
    /// Creates a response from an exception.
    /// </summary>
    public static Response FromException(Exception ex)
    {
        return new Response
        {
            IsSuccess = false,
            Message = ex.Message,
            Data = null,
        };
    }
}
