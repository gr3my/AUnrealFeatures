namespace AUnrealFeatures.Hosting.Configuration;

/// <summary>
/// Small helpers for reading process environment variables with predictable defaults.
/// </summary>
public static class EnvironmentSettings
{
    public static string Get(string name, string fallback = "")
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    public static bool GetBoolean(string name, bool fallback = false)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    public static int GetInt32(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    public static ushort GetUInt16(string name, ushort fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return ushort.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
