using System.Text.Json.Serialization;
using Sunset.Domain.Enums;

namespace Sunset.Application.DTOs.Devices;

/// <param name="Token">The Expo push token, "ExponentPushToken[...]".</param>
/// <param name="Platform">"android" or "ios".</param>
/// <param name="InstallId">Stable id of this installation of the app (optional but recommended: a changed token replaces the old row).</param>
public sealed record RegisterDeviceRequest(
    string Token,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] PushPlatform Platform,
    Guid? InstallId = null,
    string? AppVersion = null,
    string? Locale = null);

/// <summary>Either field identifies the device; at least one is required.</summary>
public sealed record RemoveDeviceRequest(string? Token = null, Guid? InstallId = null);
