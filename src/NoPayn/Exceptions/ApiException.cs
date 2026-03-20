namespace NoPayn.Exceptions;

public class ApiException(int statusCode, string message, string? errorBody = null)
    : NoPaynException($"NoPayn API error (HTTP {statusCode}): {message}")
{
    public int StatusCode { get; } = statusCode;
    public string? ErrorBody { get; } = errorBody;
}
