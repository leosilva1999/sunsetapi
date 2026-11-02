using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Moderation;

/// <summary>A partial update: what is left out (null) stays as it is.</summary>
/// <param name="Description">Replaces the description; an empty string clears it.</param>
/// <param name="Category">Replaces the category.</param>
public sealed record UpdateLocationDetailsRequest(
    string? Description = null,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] LocationCategory? Category = null);
