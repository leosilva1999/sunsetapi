namespace Sunset.Application.Common;

public sealed record CursorPagedResult<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
