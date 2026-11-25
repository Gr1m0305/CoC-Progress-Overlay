using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ClashOverlay.Tests.Support;

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>Keeps every formatted log line so tests can check what was (not) logged.</summary>
public sealed class CapturingLogger : ILogger
{
    public List<string> Lines { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Lines.Add(formatter(state, exception) + (exception is null ? "" : " " + exception));
    }
}

/// <summary>A folder under %TEMP% that is removed afterwards.</summary>
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClashOverlayTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class FakeKeys
{
    /// <summary>Something shaped like an API key (an unsigned JWT) that allows the given addresses.</summary>
    public static string Make(params string[] cidrs)
    {
        static string B64(object o) => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64(new { typ = "JWT", alg = "HS512", kid = "test" });
        var payload = B64(new
        {
            iss = "supercell",
            aud = "supercell:gameapi",
            iat = 1795564800,
            scopes = new[] { "clash" },
            limits = new object[] { new { tier = "developer/silver", type = "throttling" }, new { cidrs, type = "client" } },
        });
        return $"{header}.{payload}.c2lnbmF0dXJlLW5vdC1yZWFs";
    }
}
