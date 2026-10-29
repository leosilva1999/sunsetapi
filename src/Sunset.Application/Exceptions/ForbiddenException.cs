namespace Sunset.Application.Exceptions;

/// <summary>Authenticated but not allowed (403) - e.g. a banned account, or a moderator acting on a peer.</summary>
public class ForbiddenException(string message, IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}
