namespace Sunset.Application.Exceptions;

/// <summary>The request clashes with the current state (409). <paramref name="errors"/> carries machine-readable details when there are any.</summary>
public class ConflictException(string message, IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}
