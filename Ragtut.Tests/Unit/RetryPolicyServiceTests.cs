using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Ragtut.Core.Models;
using Ragtut.Core.Services;
using Shouldly;
using Xunit;

namespace Ragtut.Tests.Unit;

public class RetryPolicyServiceTests
{
    private ILogger<RetryPolicyService> _logger = null!;

    private RetryPolicyService CreateService(int maxRetries, int baseDelayMs = 0)
    {
        var config = new RagConfiguration
        {
            RetryPolicy = new RetryPolicyConfig
            {
                MaxRetries = maxRetries,
                BaseDelayMs = baseDelayMs,
                BackoffMultiplier = 1.0,
                MaxDelayMs = 1,
                EnableJitter = false
            }
        };

        var options = Substitute.For<IOptions<RagConfiguration>>();
        options.Value.Returns(config);

        _logger = Substitute.For<ILogger<RetryPolicyService>>();

        return new RetryPolicyService(options, _logger);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenOperationSucceeds_ShouldReturnResult()
    {
        var sut = CreateService(maxRetries: 3);

        var result = await sut.ExecuteWithRetryAsync(() => Task.FromResult(42), "test");

        result.ShouldBe(42);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenMaxRetriesIsOne_AndOperationAlwaysFails_ShouldThrowOriginalException()
    {
        var sut = CreateService(maxRetries: 1);
        var expectedException = new HttpRequestException("service unavailable");

        var ex = await Should.ThrowAsync<HttpRequestException>(
            () => sut.ExecuteWithRetryAsync(() => throw expectedException, "test"));

        ex.ShouldBeSameAs(expectedException);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenMaxRetriesIsOne_AndOperationAlwaysFails_ShouldNotThrowNullReferenceException()
    {
        var sut = CreateService(maxRetries: 1);

        var ex = await Should.ThrowAsync<Exception>(
            () => sut.ExecuteWithRetryAsync<int>(() => throw new HttpRequestException("fail"), "test"));

        ex.ShouldNotBeOfType<NullReferenceException>();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenOperationFailsThenSucceeds_ShouldRetryAndReturnResult()
    {
        var sut = CreateService(maxRetries: 3);
        var callCount = 0;

        var result = await sut.ExecuteWithRetryAsync(() =>
        {
            callCount++;
            if (callCount < 2)
                throw new HttpRequestException("transient");
            return Task.FromResult(99);
        }, "test");

        result.ShouldBe(99);
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenOperationAlwaysFails_ShouldCallOperationMaxRetriesTimes()
    {
        var sut = CreateService(maxRetries: 3);
        var callCount = 0;

        await Should.ThrowAsync<HttpRequestException>(
            () => sut.ExecuteWithRetryAsync<int>(() =>
            {
                callCount++;
                throw new HttpRequestException("always fails");
            }, "test"));

        callCount.ShouldBe(3);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenOperationAlwaysFails_ShouldThrowLastException()
    {
        var sut = CreateService(maxRetries: 3);
        var lastException = new HttpRequestException("third failure");
        var callCount = 0;

        var ex = await Should.ThrowAsync<HttpRequestException>(
            () => sut.ExecuteWithRetryAsync<int>(() =>
            {
                callCount++;
                throw callCount == 3
                    ? lastException
                    : new HttpRequestException($"failure {callCount}");
            }, "test"));

        ex.ShouldBeSameAs(lastException);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_WhenAllRetriesExhausted_ShouldLogErrorWithLastException()
    {
        var sut = CreateService(maxRetries: 3);
        var expectedException = new HttpRequestException("always fails");

        await Should.ThrowAsync<HttpRequestException>(
            () => sut.ExecuteWithRetryAsync<int>(() => throw expectedException, "test-op"));

        _logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            expectedException,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
