using System.Text.Json.Serialization;

namespace Astria.Desktop.Services;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DesktopPreferences))]
internal sealed partial class DesktopJsonContext : JsonSerializerContext;
