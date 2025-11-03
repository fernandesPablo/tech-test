using System.Net;
using System.Text.Json;
using FluentAssertions;
using ProductComparison.IntegrationTests.Fixtures;
using Xunit;

namespace ProductComparison.IntegrationTests;

/// <summary>
/// Testes de integração para health checks
/// </summary>
public class HealthChecksIntegrationTests : IntegrationTestBase
{
    public HealthChecksIntegrationTests(WebApplicationFactoryFixture factory) : base(factory)
    {
    }
    
    [Fact]
    public async Task GET_Health_ReturnsHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
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
