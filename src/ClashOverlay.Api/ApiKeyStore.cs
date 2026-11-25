using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClashOverlay.Api;

/// <summary>Small files encrypted with DPAPI for the current Windows user.</summary>
public static class ProtectedFile
{
    public static void Write(string path, byte[] plain, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI needs Windows.");
        }

        var data = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Null when the file is missing or was written by another user or machine.</summary>
    public static byte[]? Read(string path, byte[] entropy)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser);
        }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

/// <summary>The player's API key in <c>apikey.bin</c>, DPAPI-protected (CurrentUser).</summary>
public sealed class ApiKeyStore
{
    private static readonly byte[] Entropy = "ClashOverlay.ApiKey.v1"u8.ToArray();
    private readonly string _path;
    private string? _cached;
    private bool _loaded;

    public ApiKeyStore(string path)
    {
        _path = path;
    }

    public bool HasKey => Load() is not null;

    public event EventHandler? Changed;

    public string? Load()
    {
        if (!_loaded)
        {
            _cached = ProtectedFile.Read(_path, Entropy) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;
            _loaded = true;
        }

        return _cached;
    }

    public void Save(string key)
    {
        var clean = Normalize(key);
        if (clean.Length == 0)
        {
            throw new ArgumentException("The key is empty.", nameof(key));
        }

        ProtectedFile.Write(_path, Encoding.UTF8.GetBytes(clean), Entropy);
        _cached = clean;
        _loaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete()
    {
        ProtectedFile.Delete(_path);
        _cached = null;
        _loaded = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Trims spaces, quotes and a pasted "Bearer " prefix.</summary>
    public static string Normalize(string? key)
    {
        var k = (key ?? "").Trim().Trim('"', '\'').Trim();
        if (k.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            k = k[7..].Trim();
        }

        return string.Concat(k.Where(c => !char.IsWhiteSpace(c)));
    }
}

/// <summary>
/// What a key's own payload says. API keys are signed JWTs whose payload lists the IP addresses they allow, so the app
/// can tell which addresses a key was made for without asking the API. Nothing here is a secret check: the API decides.
/// </summary>
public sealed record ApiKeyInfo(IReadOnlyList<string> AllowedIps, DateTimeOffset? IssuedAt)
{
    public static bool LooksLikeKey(string? key) =>
        key is not null && key.StartsWith("eyJ", StringComparison.Ordinal) && key.Count(c => c == '.') == 2 && key.Length > 40;

    public static ApiKeyInfo? TryParse(string? key)
    {
        if (!LooksLikeKey(key))
        {
            return null;
        }

        try
        {
            var part = key!.Split('.')[1].Replace('-', '+').Replace('_', '/');
            part = part.PadRight(part.Length + ((4 - (part.Length % 4)) % 4), '=');
            using var doc = JsonDocument.Parse(Convert.FromBase64String(part));
            var root = doc.RootElement;
            var ips = new List<string>();
            if (root.TryGetProperty("limits", out var limits) && limits.ValueKind == JsonValueKind.Array)
            {
                foreach (var limit in limits.EnumerateArray())
                {
                    if (limit.TryGetProperty("cidrs", out var cidrs) && cidrs.ValueKind == JsonValueKind.Array)
                    {
                        ips.AddRange(cidrs.EnumerateArray().Select(c => c.GetString()).OfType<string>());
                    }
                }
            }

            DateTimeOffset? issued = root.TryGetProperty("iat", out var iat) && iat.TryGetInt64(out var secs)
                ? DateTimeOffset.FromUnixTimeSeconds(secs)
                : null;
            return new ApiKeyInfo(ips, issued);
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public bool Allows(string ip) => AllowedIps.Any(c => CidrContains(c, ip));

    /// <summary>True for an exact address or an address inside <c>a.b.c.d/n</c>.</summary>
    public static bool CidrContains(string cidr, string ip)
    {
        var slash = cidr.IndexOf('/');
        var network = slash < 0 ? cidr : cidr[..slash];
        if (!IPAddress.TryParse(network, out var net) || !IPAddress.TryParse(ip, out var addr) || net.AddressFamily != addr.AddressFamily)
        {
            return false;
        }

        var bits = net.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (slash >= 0 && (!int.TryParse(cidr[(slash + 1)..], out bits) || bits < 0))
        {
            return false;
        }

        var a = net.GetAddressBytes();
        var b = addr.GetAddressBytes();
        for (var i = 0; i < a.Length && bits > 0; i++, bits -= 8)
        {
            var mask = bits >= 8 ? 0xFF : (byte)(0xFF << (8 - bits));
            if ((a[i] & mask) != (b[i] & mask))
            {
                return false;
            }
        }

        return true;
    }
}
