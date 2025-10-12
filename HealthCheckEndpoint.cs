using System.Net;
using System.Text;
using System.Text.Json;
using Serilog;

namespace MustMail;

/// <summary>
/// Simple HTTP server for health check endpoint
/// </summary>
public class HealthCheckEndpoint
{
    private readonly HttpListener _listener;
    private readonly int _port;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;

    public HealthCheckEndpoint(int port, ILogger logger)
    {
        _port = port;
        _logger = logger;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{port}/");
    }

    /// <summary>
    /// Start the health check HTTP listener
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            _listener.Start();
            _logger.Information("Health check endpoint started on port {Port}", _port);

            await ListenAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to start health check endpoint");
            throw;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing health check request");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Only respond to /health or /healthz paths
            if (request.Url?.AbsolutePath == "/health" || request.Url?.AbsolutePath == "/healthz")
            {
                var healthStatus = HealthCheckService.Instance.GetHealthStatus();

                // Set status code based on health
                response.StatusCode = healthStatus.IsHealthy ? 200 : 503;
                response.ContentType = "application/json";

                var jsonResponse = JsonSerializer.Serialize(new
                {
                    status = healthStatus.IsHealthy ? "healthy" : "unhealthy",
                    totalAttempts = healthStatus.TotalAttempts,
                    successfulAttempts = healthStatus.SuccessfulAttempts,
                    failedAttempts = healthStatus.FailedAttempts,
                    lastError = healthStatus.LastError,
                    lastErrorTime = healthStatus.LastErrorTime?.ToString("o")
                }, new JsonSerializerOptions { WriteIndented = true });

                var buffer = Encoding.UTF8.GetBytes(jsonResponse);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
            }
            else
            {
                response.StatusCode = 404;
                var buffer = Encoding.UTF8.GetBytes("Not Found");
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error handling health check request");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }

    /// <summary>
    /// Stop the health check HTTP listener
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
        _listener.Close();
        _logger.Information("Health check endpoint stopped");
    }
}
