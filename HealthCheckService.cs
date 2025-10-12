using System.Collections.Concurrent;

namespace MustMail;

/// <summary>
/// Singleton service that tracks SMTP relay health status
/// </summary>
public class HealthCheckService
{
    private static readonly Lazy<HealthCheckService> _instance = new(() => new HealthCheckService());

    private readonly object _lock = new();
    private bool _hasError = false;
    private string? _lastError = null;
    private DateTime? _lastErrorTime = null;
    private int _totalAttempts = 0;
    private int _successfulAttempts = 0;
    private int _failedAttempts = 0;

    public static HealthCheckService Instance => _instance.Value;

    private HealthCheckService() { }

    /// <summary>
    /// Record a successful SMTP send
    /// </summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _totalAttempts++;
            _successfulAttempts++;
        }
    }

    /// <summary>
    /// Record a failed SMTP send with error details
    /// </summary>
    public void RecordFailure(string errorMessage)
    {
        lock (_lock)
        {
            _totalAttempts++;
            _failedAttempts++;
            _hasError = true;
            _lastError = errorMessage;
            _lastErrorTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Get current health status
    /// </summary>
    public HealthStatus GetHealthStatus()
    {
        lock (_lock)
        {
            return new HealthStatus
            {
                IsHealthy = !_hasError,
                TotalAttempts = _totalAttempts,
                SuccessfulAttempts = _successfulAttempts,
                FailedAttempts = _failedAttempts,
                LastError = _lastError,
                LastErrorTime = _lastErrorTime
            };
        }
    }
}

/// <summary>
/// Health status data
/// </summary>
public class HealthStatus
{
    public bool IsHealthy { get; set; }
    public int TotalAttempts { get; set; }
    public int SuccessfulAttempts { get; set; }
    public int FailedAttempts { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorTime { get; set; }
}
