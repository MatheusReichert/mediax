namespace Mediax.Core;

public enum ErrorType
{
    NotFound,
    Validation,
    Conflict,
    Internal
}

/// <summary>Represents a structured error with a code, message, type, and optional field-level details.</summary>
public sealed class Error
{
    public string Code { get; }
    public string Message { get; }
    public ErrorType Type { get; }
    public IReadOnlyDictionary<string, string[]>? Details { get; }

    private Error(string code, string message, ErrorType type, IReadOnlyDictionary<string, string[]>? details = null)
    {
        Code = code;
        Message = message;
        Type = type;
        Details = details;
    }

    public static Error NotFound(string code, string? message = null)
        => new(code, message ?? $"Resource '{code}' was not found.", ErrorType.NotFound);

    public static Error Validation(string code, string? message = null, IReadOnlyDictionary<string, string[]>? details = null)
        => new(code, message ?? $"Validation failed for '{code}'.", ErrorType.Validation, details);

    public static Error Conflict(string code, string? message = null)
        => new(code, message ?? $"Conflict detected for '{code}'.", ErrorType.Conflict);

    public static Error Internal(string code, string? message = null)
        => new(code, message ?? $"An internal error occurred: '{code}'.", ErrorType.Internal);

    public override string ToString() => $"[{Type}] {Code}: {Message}";
}
