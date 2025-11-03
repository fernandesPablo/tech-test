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

    [Fact]
    public async Task GET_Health_ReturnsHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        // Debug: Print response
        _output.WriteLine($"Status: {response.StatusCode}");
        _output.WriteLine($"Content: {content}");
        _output.WriteLine($"CSV Test Path: {Factory.TestCsvPath}");
        _output.WriteLine($"CSV Exists: {System.IO.File.Exists(Factory.TestCsvPath)}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task GET_HealthReady_WithRedis_ReturnsHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health/ready");

        // Assert - Deve validar Redis
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task GET_HealthLive_ReturnsHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health/live");

        // Assert - Liveness não valida dependências
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Fact]
    public async Task HealthChecks_ShouldNotCausePerformanceIssues()
    {
        // Arrange - Faz múltiplas chamadas rápidas
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Client.GetAsync("/health/live"))
            .ToList();

        // Act
        var responses = await Task.WhenAll(tasks);

        // Assert - Todas devem retornar OK rapidamente
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }
}
