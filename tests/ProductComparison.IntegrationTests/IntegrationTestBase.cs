using ProductComparison.IntegrationTests.Fixtures;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace ProductComparison.IntegrationTests;

/// <summary>
/// Classe base para testes de integração com limpeza de dados
/// </summary>
public abstract class IntegrationTestBase : IClassFixture<WebApplicationFactoryFixture>, IAsyncLifetime
{
    protected readonly WebApplicationFactoryFixture Factory;
    protected readonly HttpClient Client;

    protected IntegrationTestBase(WebApplicationFactoryFixture factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    /// <summary>
    /// Inicialização antes de cada teste
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        // Reseta CSV para dados iniciais
        Factory.ResetTestData();
        
        // Limpa cache Redis
        await ClearRedisCache();
        
        // Aguarda um momento para garantir que as mudanças foram aplicadas
        await Task.Delay(100);
    }

    /// <summary>
    /// Limpeza após cada teste
    /// </summary>
    public virtual Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Limpa todo o cache Redis
    /// </summary>
    private async Task ClearRedisCache()
    {
        try
        {
            var connectionString = Factory.Services
                .GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()
                .GetConnectionString("RedisConnection");

            if (!string.IsNullOrEmpty(connectionString))
            {
                var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
                var db = redis.GetDatabase();
                var endpoints = redis.GetEndPoints();
                
                foreach (var endpoint in endpoints)
                {
                    var server = redis.GetServer(endpoint);
                    await server.FlushDatabaseAsync();
                }
                
                await redis.CloseAsync();
            }
        }
        catch
        {
            // Se falhar ao limpar cache, ignora (pode não ter Redis disponível)
        }
    }
}
