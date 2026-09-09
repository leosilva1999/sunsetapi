using System.Text.Json.Serialization;

namespace Sunset.Application.Common;

/// <summary>
/// Distinguishes a field omitted from a PATCH body (<see cref="IsSet"/> false, leave unchanged)
/// from one explicitly sent as null (<see cref="IsSet"/> true, <see cref="Value"/> null, clear
/// it) - a plain nullable property collapses both into the same `null` and can't tell them apart.
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public bool IsSet { get; }
    public T? Value { get; }

    private Optional(bool isSet, T? value)
    {
        IsSet = isSet;
        Value = value;
    }

    public static readonly Optional<T> Unset = new(false, default);

    public static Optional<T> Of(T? value) => new(true, value);
}
