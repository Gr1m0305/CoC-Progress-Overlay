using ClashOverlay.Core;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>
/// Rolling log files in <c>%LOCALAPPDATA%\ClashOverlay\logs</c>: <c>clashoverlay-DATE.log</c> for the app and
/// <c>demo-DATE.log</c> for a demo, so each file has one writer. Anything that looks like a key is redacted. Problems
/// with the log itself go to <c>logging-problems.txt</c> beside it.
/// </summary>
internal static class AppLogging
{
    private const long ProblemFileLimit = 256 * 1024;

    public static ILoggerFactory Create(string logDir, bool verbose, bool demo = false)
    {
        Directory.CreateDirectory(logDir);
        var problems = Path.Combine(logDir, "logging-problems.txt");
        SelfLog.Enable(message =>
        {
            try
            {
                if (!File.Exists(problems) || new FileInfo(problems).Length < ProblemFileLimit)
                {
                    File.AppendAllText(problems, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} {LogScrubber.Scrub(message)}{Environment.NewLine}");
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        });

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .Enrich.With<ScrubEnricher>()
            .WriteTo.File(
                Path.Combine(logDir, demo ? "demo-.log" : "clashoverlay-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        Log.Logger = serilog;
        return new SerilogLoggerFactory(serilog, dispose: true);
    }

    /// <summary>Redacts key-like text in every string property before it is rendered.</summary>
    private sealed class ScrubEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            foreach (var (name, value) in logEvent.Properties.ToList())
            {
                if (value is ScalarValue { Value: string s } && LogScrubber.NeedsScrub(s))
                {
                    logEvent.AddOrUpdateProperty(new LogEventProperty(name, new ScalarValue(LogScrubber.Scrub(s))));
                }
            }
        }
    }
}
