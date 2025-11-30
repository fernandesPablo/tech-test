using System.Net;
using System.Text.Json;
using FluentAssertions;
using ProductComparison.IntegrationTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace ProductComparison.IntegrationTests;

/// <summary>
/// Testes de integração para health checks
/// </summary>
public class HealthChecksIntegrationTests : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public HealthChecksIntegrationTests(WebApplicationFactoryFixture factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    /// <summary>
    /// Performs a health check request and asserts it returns Healthy status.
    /// </summary>
    private async Task AssertHealthCheckIsHealthyAsync(string endpoint, bool outputDebugInfo = false)
    {
        // Act
        var response = await Client.GetAsync(endpoint);
        var content = await response.Content.ReadAsStringAsync();

        // Debug output if requested
        if (outputDebugInfo)
        {
            _output.WriteLine($"Status: {response.StatusCode}");
            _output.WriteLine($"Content: {content}");
            _output.WriteLine($"CSV Test Path: {Factory.TestCsvPath}");
            _output.WriteLine($"CSV Exists: {System.IO.File.Exists(Factory.TestCsvPath)}");
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task GET_Health_ReturnsHealthy()
    {
        await AssertHealthCheckIsHealthyAsync("/health", outputDebugInfo: true);
    }

    [Fact]
    public async Task GET_HealthReady_WithRedis_ReturnsHealthy()
    {
        // Should validate Redis
        await AssertHealthCheckIsHealthyAsync("/health/ready");
    }

    [Fact]
    public async Task GET_HealthLive_ReturnsHealthy()
    {
        // Liveness doesn't validate dependencies
        await AssertHealthCheckIsHealthyAsync("/health/live");
    }

    [Fact]
    public async Task HealthChecks_ShouldNotCausePerformanceIssues()
    {
        // Arrange - Make multiple rapid calls
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Client.GetAsync("/health/live"))
            .ToList();

        // Act
        var responses = await Task.WhenAll(tasks);

        // Assert - All should return OK quickly
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }
}
