using ClashOverlay.Core;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace ClashOverlay.App.Services;

/// <summary>Rolling log files in <c>%LOCALAPPDATA%\ClashOverlay\logs</c>. Anything that looks like a key is redacted.</summary>
internal static class AppLogging
{
    public static ILoggerFactory Create(string logDir, bool verbose)
    {
        Directory.CreateDirectory(logDir);
        var serilog = new LoggerConfiguration()
            .MinimumLevel.Is(verbose ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .Enrich.With<ScrubEnricher>()
            .WriteTo.File(
                Path.Combine(logDir, "clashoverlay-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
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
