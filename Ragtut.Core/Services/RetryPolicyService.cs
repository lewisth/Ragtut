using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ragtut.Core.Models;
using System.Net;
using System.Net.Sockets;

namespace Ragtut.Core.Services;

/// <summary>
/// Service for handling retry policies with exponential backoff and jitter
/// </summary>
public class RetryPolicyService
{
    private readonly RetryPolicyConfig _config;
    private readonly ILogger<RetryPolicyService> _logger;
    private readonly Random _random = new();

    public RetryPolicyService(IOptions<RagConfiguration> options, ILogger<RetryPolicyService> logger)
    {
        _config = options.Value.RetryPolicy;
        _logger = logger;
    }

    /// <summary>
    /// Executes an operation with retry policy
    /// </summary>
    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var attempt = 0;
        Exception lastException = null!;

        while (attempt < _config.MaxRetries)
        {
            try
            {
                _logger.LogDebug("Executing {OperationName}, attempt {Attempt}/{MaxRetries}", 
                    operationName, attempt + 1, _config.MaxRetries);

                return await operation();
            }
            catch (Exception ex) when (IsRetriableException(ex))
            {
                lastException = ex;
                attempt++;

                if (attempt >= _config.MaxRetries)
                {
                    break;
                }

                var delay = CalculateDelay(attempt);
                _logger.LogWarning(ex, "Operation {OperationName} failed on attempt {Attempt}, retrying in {Delay}ms", 
                    operationName, attempt, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }

        _logger.LogError(lastException, "Operation {OperationName} failed after {MaxRetries} attempts", 
            operationName, _config.MaxRetries);
        throw lastException!;
    }

    /// <summary>
    /// Executes an operation with retry policy (void return)
    /// </summary>
    public async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true;
        }, operationName, cancellationToken);
    }

    /// <summary>
    /// Executes an HTTP operation with retry policy, considering HTTP status codes
    /// </summary>
    public async Task<HttpResponseMessage> ExecuteHttpWithRetryAsync(
        Func<Task<HttpResponseMessage>> httpOperation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithRetryAsync(async () =>
        {
            var response = await httpOperation();
            
            // Consider certain HTTP status codes as retriable
            if (!response.IsSuccessStatusCode && IsRetriableHttpStatus(response.StatusCode))
            {
                throw new HttpRequestException($"HTTP request failed with status code {response.StatusCode}");
            }

            return response;
        }, operationName, cancellationToken);
    }

    private bool IsRetriableException(Exception exception)
    {
        var exceptionTypeName = exception.GetType().Name;
        
        // Check if the exception type is in the retriable list
        if (_config.RetriableExceptions.Contains(exceptionTypeName))
        {
            return true;
        }

        // Check for specific common retriable exceptions
        return exception switch
        {
            HttpRequestException => true,
            TaskCanceledException => true,
            TimeoutException => true,
            SocketException => true,
            _ when exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => true,
            _ when exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }

    private bool IsRetriableHttpStatus(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.RequestTimeout => true,
            HttpStatusCode.TooManyRequests => true,
            HttpStatusCode.InternalServerError => true,
            HttpStatusCode.BadGateway => true,
            HttpStatusCode.ServiceUnavailable => true,
            HttpStatusCode.GatewayTimeout => true,
            _ => false
        };
    }

    private int CalculateDelay(int attempt)
    {
        // Calculate exponential backoff
        var delay = (int)(_config.BaseDelayMs * Math.Pow(_config.BackoffMultiplier, attempt - 1));
        
        // Apply maximum delay limit
        delay = Math.Min(delay, _config.MaxDelayMs);

        // Add jitter if enabled
        if (_config.EnableJitter)
        {
            var jitter = _random.Next(0, delay / 4); // Up to 25% jitter
            delay = delay - jitter / 2 + jitter;
        }

        return delay;
    }
} 