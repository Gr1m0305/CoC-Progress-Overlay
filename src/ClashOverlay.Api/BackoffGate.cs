namespace ClashOverlay.Api;

/// <summary>
/// Stops the client from calling the API for a while after it said no: honours <c>Retry-After</c>, doubles the wait on
/// repeated failures, and pauses on key problems until the key changes.
/// </summary>
public sealed class BackoffGate
{
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private DateTimeOffset _until = DateTimeOffset.MinValue;
    private int _failures;

    public BackoffGate(TimeProvider time)
    {
        _time = time;
    }

    public ClashApiException? LastError { get; private set; }

    public DateTimeOffset? ClosedUntil
    {
        get
        {
            lock (_lock)
            {
                return _time.GetUtcNow() < _until ? _until : null;
            }
        }
    }

    public bool IsClosed => ClosedUntil is not null;

    public void Success()
    {
        lock (_lock)
        {
            _failures = 0;
            _until = DateTimeOffset.MinValue;
            LastError = null;
        }
    }

    public void Failure(ClashApiException error)
    {
        lock (_lock)
        {
            var wait = WaitFor(error, _failures + 1);
            if (wait <= TimeSpan.Zero)
            {
                return; // e.g. a wrong tag: nothing to back off from
            }

            _failures++;
            LastError = error;
            var until = _time.GetUtcNow() + wait;
            if (until > _until)
            {
                _until = until;
            }
        }
    }

    public void Reset() => Success();

    internal static TimeSpan WaitFor(ClashApiException error, int failures)
    {
        static TimeSpan Doubling(TimeSpan first, int n, TimeSpan cap)
        {
            var seconds = first.TotalSeconds * Math.Pow(2, Math.Min(n - 1, 16));
            return TimeSpan.FromSeconds(Math.Min(seconds, cap.TotalSeconds));
        }

        return error.Kind switch
        {
            ApiErrorKind.Throttled => error.RetryAfter ?? Doubling(TimeSpan.FromSeconds(5), failures, TimeSpan.FromMinutes(5)),
            ApiErrorKind.Maintenance => Max(error.RetryAfter, Doubling(TimeSpan.FromMinutes(1), failures, TimeSpan.FromMinutes(10))),
            ApiErrorKind.ServerError or ApiErrorKind.Network or ApiErrorKind.Timeout =>
                Max(error.RetryAfter, Doubling(TimeSpan.FromSeconds(10), failures, TimeSpan.FromMinutes(5))),
            ApiErrorKind.InvalidIp or ApiErrorKind.InvalidKey => TimeSpan.FromMinutes(5),
            _ => TimeSpan.Zero,
        };
    }

    private static TimeSpan Max(TimeSpan? a, TimeSpan b) => a is { } x && x > b ? x : b;
}
