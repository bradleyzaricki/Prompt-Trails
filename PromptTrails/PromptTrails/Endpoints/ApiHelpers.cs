using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PromptTrails.Endpoints;

/// <summary>Small shared helpers for the frontend-facing read endpoints.</summary>
internal static class ApiHelpers
{
    /// <summary>The authenticated user's id from the token's 'sub' claim (mirrors Program.cs CurrentUserId).</summary>
    public static long UserId(this ClaimsPrincipal me) =>
        long.Parse(me.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    /// <summary>Re-parse a jsonb string column into a JSON value for the response (null/blank -> null).</summary>
    public static JsonElement? AsJson(string? jsonb) =>
        string.IsNullOrWhiteSpace(jsonb) ? null : JsonSerializer.Deserialize<JsonElement>(jsonb);

    /// <summary>First non-empty line of text, trimmed and length-capped — a display title / excerpt.</summary>
    public static string FirstLine(string? text, int max = 140)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(empty prompt)";
        var line = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.Length > 0) ?? text.Trim();
        return line.Length <= max ? line : line[..max] + "…";
    }
}

/// <summary>
/// Opaque keyset-pagination cursor: the last row's sort value + its id tiebreaker, URL-safe base64.
/// Clients treat it as opaque and echo it back as <c>?cursor=</c> to fetch the next page.
/// </summary>
internal static class Cursor
{
    public static string Encode(string sortValue, long id)
    {
        var bytes = Encoding.UTF8.GetBytes($"{sortValue}|{id}");
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static (string sortValue, long id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var s = cursor.Replace('-', '+').Replace('_', '/');
            s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(s));
            var i = raw.LastIndexOf('|');
            if (i < 0) return null;
            return (raw[..i], long.Parse(raw[(i + 1)..]));
        }
        catch { return null; }
    }
}
