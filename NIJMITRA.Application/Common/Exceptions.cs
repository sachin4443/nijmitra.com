namespace NIJMITRA.Application.Common;

public abstract class AppException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string what = "Record") : AppException($"{what} was not found.", "not_found");
public sealed class ForbiddenException(string message = "You do not have permission to perform this action.", string code = "forbidden") : AppException(message, code);
public sealed class ConflictException(string message) : AppException(message, "conflict");
public sealed class UnauthorizedAppException(string message, string code = "unauthorized") : AppException(message, code);

public sealed class ValidationAppException : AppException
{
    public IDictionary<string, string[]> Errors { get; }
    public ValidationAppException(string message) : base(message, "validation_failed")
        => Errors = new Dictionary<string, string[]>();
    public ValidationAppException(string field, string message) : base(message, "validation_failed")
        => Errors = new Dictionary<string, string[]> { [field] = [message] };
    public ValidationAppException(IDictionary<string, string[]> errors) : base("One or more validation errors occurred.", "validation_failed")
        => Errors = errors;
}

/// <summary>Raised when a sensitive operation needs a fresh PIN/password confirmation.</summary>
public sealed class ReauthenticationRequiredException() : AppException("Please confirm your PIN or password to continue.", "reauth_required");
