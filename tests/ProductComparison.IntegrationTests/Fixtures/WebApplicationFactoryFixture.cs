using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductComparison.Infrastructure.BackgroundServices;
using ProductComparison.Infrastructure.Configuration;
using ProductComparison.Infrastructure.HealthChecks;
using Testcontainers.Redis;

namespace ProductComparison.IntegrationTests.Fixtures;

/// <summary>
/// Fixture para criar um servidor de testes com API completa e Redis container
/// </summary>
public class WebApplicationFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private RedisContainer? _redisContainer;
    private readonly string _testCsvPath;

    public WebApplicationFactoryFixture()
    {
        // Cria path do CSV ANTES da configuração do WebHost
        _testCsvPath = Path.Combine(Path.GetTempPath(), $"test-products-{Guid.NewGuid()}.csv");

        // Cria o arquivo CSV imediatamente
        ResetTestData();
    }

    /// <summary>
    /// Caminho do CSV de teste
    /// </summary>
    public string TestCsvPath => _testCsvPath;

    /// <summary>
    /// Inicializa recursos async (Redis container e CSV de teste)
    /// </summary>
    public async Task InitializeAsync()
    {
        // Cria e inicia container Redis para testes
        _redisContainer = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithPortBinding(6379, true) // Porta aleatória no host
            .Build();

        await _redisContainer.StartAsync();
    }

    /// <summary>
    /// Reseta dados do CSV para estado inicial
    /// </summary>
    public void ResetTestData()
    {
        var originalCsvPath = Path.Combine(AppContext.BaseDirectory, "Data", "test-products.csv");
        if (File.Exists(originalCsvPath))
        {
            File.Copy(originalCsvPath, _testCsvPath, overwrite: true);
        }
        else
        {
            // Fallback: cria CSV com dados de teste se não existir
            var csvLines = new[]
            {
                "Id,Name,Description,ImageUrl,Price,Rating,Brand,Color,Weight,Version",
                "1,iPhone 13 Pro,Smartphone Apple com câmera profissional,https://example.com/iphone13.jpg,4999.99,4.8,Apple,Grafite,238,1",
                "2,Galaxy S21,Smartphone Samsung com tela 120Hz,https://example.com/s21.jpg,3799.99,4.6,Samsung,Preto,171,1",
                "3,Notebook Dell XPS,Notebook premium com Intel i7,https://example.com/xps.jpg,8499.99,4.9,Dell,Prata,1800,1",
                "4,PlayStation 5,Console de última geração,https://example.com/ps5.jpg,3999.99,4.7,Sony,Branco,4500,1",
                "5,AirPods Pro,Fones de ouvido com cancelamento de ruído,https://example.com/airpods.jpg,1299.99,4.5,Apple,Branco,54,1"
            };
            File.WriteAllLines(_testCsvPath, csvLines, System.Text.Encoding.UTF8);
        }
    }

    /// <summary>
    /// Configura o WebHost para testes
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Remove configurações existentes
            config.Sources.Clear();

            // Adiciona configurações de teste
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RedisConnection"] = _redisContainer?.GetConnectionString() ?? "localhost:6379",
                ["RepositoryConfiguration:CsvFilePath"] = _testCsvPath,
                ["CsvBackup:BackupIntervalMinutes"] = "60",
                ["CsvBackup:MaxBackups"] = "5",
                ["Serilog:MinimumLevel:Default"] = "Warning",
                ["Logging:LogLevel:Default"] = "Warning",
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning"
            });
        });

        builder.ConfigureServices((context, services) =>
        {
            // SOBRESCREVE RepositoryConfiguration com valores de teste
            var existingConfig = services.FirstOrDefault(d => d.ServiceType == typeof(RepositoryConfiguration));
            if (existingConfig != null)
            {
                services.Remove(existingConfig);
            }

            // Registra com CsvFilePath correto para testes (mantém outras propriedades)
            var testConfig = new RepositoryConfiguration
            {
                BaseDirectory = AppContext.BaseDirectory,
                CsvFolder = "Csv",
                ProductsFileName = "products.csv",
                CsvFilePath = _testCsvPath
            };
            services.AddSingleton(testConfig);

            // Remove CsvBackupService durante testes (evita race conditions)
            var backupServiceDescriptor = services.FirstOrDefault(d =>
                d.ServiceType == typeof(IHostedService) &&
                d.ImplementationType == typeof(CsvBackupService));

            if (backupServiceDescriptor != null)
            {
                services.Remove(backupServiceDescriptor);
            }

            // Configura logging para testes
            services.AddLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Warning);
            });
        });

        builder.UseEnvironment("Testing");
    }

    /// <summary>
    /// Limpa recursos async (container Redis e arquivo CSV temporário)
    /// </summary>
    public new async Task DisposeAsync()
    {
        if (_redisContainer != null)
        {
            await _redisContainer.StopAsync();
            await _redisContainer.DisposeAsync();
        }

        // Limpa arquivo CSV temporário
        if (!string.IsNullOrEmpty(_testCsvPath) && File.Exists(_testCsvPath))
        {
            try
            {
                File.Delete(_testCsvPath);
            }
            catch
            {
                // Ignora erros ao deletar arquivo temporário
            }
        }

        await base.DisposeAsync();
    }
}
