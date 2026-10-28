using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AdPulse.Api.Tenancy;

/// <summary>Key generation + hashing. Keys look like <c>adp_live_…</c> (32 random bytes, URL-safe base64).</summary>
public static partial class ApiKeys
{
    public const string Prefix = "adp_live_";

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Prefix + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Lower-case hex SHA-256. Constant-time comparison is unnecessary: lookup is by hash equality in the DB.</summary>
    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.Trim()))).ToLowerInvariant();

    public static string DisplayPrefix(string key) => key.Length <= 12 ? key : key[..12];

    /// <summary>"Acme Retail!" -> "acme-retail". Falls back to "workspace" for names with no usable characters.</summary>
    public static string Slugify(string name)
    {
        var s = NonSlug().Replace(name.Trim().ToLowerInvariant(), "-").Trim('-');
        s = Dashes().Replace(s, "-");
        if (s.Length > 40) s = s[..40].TrimEnd('-');
        return string.IsNullOrEmpty(s) ? "workspace" : s;
    }

    public static string RandomSuffix(int len = 4)
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
        return string.Create(len, alphabet, (span, a) => { for (var i = 0; i < span.Length; i++) span[i] = a[RandomNumberGenerator.GetInt32(a.Length)]; });
    }

    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonSlug();
    [GeneratedRegex("-{2,}")] private static partial Regex Dashes();
}
