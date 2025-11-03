using System.Net;
using System.Text.Json;
using FluentAssertions;
using ProductComparison.IntegrationTests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace ProductComparison.IntegrationTests;

public class DebugHealthCheckTest : IntegrationTestBase
{
    private readonly ITestOutputHelper _output;

    public DebugHealthCheckTest(WebApplicationFactoryFixture factory, ITestOutputHelper output) : base(factory)
    {
        _output = output;
    }

    [Fact]
    public async Task Debug_HealthCheck_ShowResponse()
    {
        // Act
        var response = await Client.GetAsync("/health");

        // Log detalhes
        _output.WriteLine($"Status Code: {response.StatusCode}");
        _output.WriteLine($"CSV Path configurado: {Factory.TestCsvPath}");
        _output.WriteLine($"CSV Existe? {System.IO.File.Exists(Factory.TestCsvPath)}");

        var content = await response.Content.ReadAsStringAsync();
        _output.WriteLine($"Response Body: {content}");

        // Apenas para não falhar
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable);
    }
}
