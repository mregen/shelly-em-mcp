// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace ShellyEmMcp.Devices;

/// <summary>
/// Small shared helpers for pulling optional, loosely-typed fields out of a Shelly RPC response -
/// used by every Tools class instead of <see cref="JsonSerializer.Deserialize{T}"/> since the RPC
/// responses are only ever reshaped/projected, never round-tripped.
/// </summary>
internal static class ShellyJson
{
    public static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    public static double? GetDouble(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    public static bool? GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    public static JsonElement? GetObject(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;
}
