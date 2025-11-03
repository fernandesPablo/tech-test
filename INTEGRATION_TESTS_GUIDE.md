# 🧪 Guia de Testes de Integração

## 📚 O que são Testes de Integração?

Testes de integração validam que **múltiplos componentes funcionam corretamente quando integrados**, sem usar mocks. Eles testam o sistema como um todo, incluindo:

- ✅ API endpoints (HTTP requests reais)
- ✅ Banco de dados / CSV (leitura/escrita real)
- ✅ Cache (Redis real)
- ✅ Serialização JSON
- ✅ Middleware (exception handling, logging, rate limiting)
- ✅ Configurações (appsettings.json)

---

## 🎯 Por que Tech Leads se Importam?

### Problema Real em Produção

**Cenário:**
```csharp
// ✅ Todos os 14 testes unitários passaram
dotnet test
// Test Run Successful. Total tests: 14, Passed: 14

// Deploy para produção...
// ❌ BOOM! Aplicação quebra:
// "FileNotFoundException: products.csv not found"
// "RedisConnectionException: Unable to connect to Redis"
```

**Por quê?**
- Os testes unitários usavam **mocks** (objetos fake)
- Nunca testaram se o CSV **realmente existe** no path correto
- Nunca testaram se o Redis **realmente conecta**

### Com Testes de Integração

```csharp
// ✅ Testes unitários passaram (14 tests)
// ✅ Testes de integração passaram (8 tests)
//    - CSV foi lido com sucesso
//    - Redis conectou e cacheou dados
//    - Endpoints retornaram 200 OK
//    - JSON serializou corretamente

// Deploy para produção...
// ✅ Aplicação funciona perfeitamente!
```

---

## 🏗️ Estrutura do Projeto

```
tests/
├── ProductComparison.UnitTests/           # ✅ Existente (14 testes)
│   └── ProductServiceTests.cs
│
└── ProductComparison.IntegrationTests/    # ⭐ NOVO (adicionar)
    ├── ProductComparison.IntegrationTests.csproj
    ├── Fixtures/
    │   └── WebApplicationFactoryFixture.cs    # Setup da API
    ├── Data/
    │   └── test-products.csv                  # CSV de teste
    ├── ProductsControllerTests.cs             # Testa endpoints
    ├── CacheIntegrationTests.cs               # Testa Redis
    ├── HealthChecksIntegrationTests.cs        # Testa /health
    └── RateLimitingIntegrationTests.cs        # Testa rate limit
```

---

## 🛠️ Ferramentas Necessárias

### 1. WebApplicationFactory (Microsoft)
Sobe a aplicação completa em memória para testes.

```csharp
// Cria um servidor de teste
var factory = new WebApplicationFactory<Program>();
var client = factory.CreateClient();

// Faz requisição HTTP REAL
var response = await client.GetAsync("/api/v1/products");
```

### 2. Testcontainers (opcional mas recomendado)
Sobe containers Docker durante os testes (Redis, PostgreSQL, etc).

```csharp
// Sobe Redis em container para testes
var redisContainer = new TestcontainersBuilder<RedisTestcontainer>()
    .WithImage("redis:7-alpine")
    .Build();

await redisContainer.StartAsync();
```

### 3. FluentAssertions (opcional)
Torna assertions mais legíveis.

```csharp
// Ao invés de:
Assert.Equal(200, (int)response.StatusCode);

// Use:
response.StatusCode.Should().Be(HttpStatusCode.OK);
products.Items.Should().HaveCount(10);
```

---

## 📦 Packages Necessários

```xml
<ItemGroup>
  <!-- Test Framework -->
  <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.0.0" />
  <PackageReference Include="xunit" Version="2.9.2" />
  <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  
  <!-- Testcontainers (para Redis real) -->
  <PackageReference Include="Testcontainers.Redis" Version="3.10.0" />
  
  <!-- Assertions mais legíveis -->
  <PackageReference Include="FluentAssertions" Version="6.12.1" />
  
  <!-- Test SDK -->
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
</ItemGroup>
```

---

## 🚀 Exemplo Completo

### 1. Setup do WebApplicationFactory

```csharp
// Fixtures/WebApplicationFactoryFixture.cs
public class WebApplicationFactoryFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private RedisTestcontainer? _redisContainer;
    
    public async Task InitializeAsync()
    {
        // Sobe Redis em container
        _redisContainer = new TestcontainersBuilder<RedisTestcontainer>()
            .WithImage("redis:7-alpine")
            .WithPortBinding(6379, true)
            .Build();
        
        await _redisContainer.StartAsync();
    }
    
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Usa appsettings.Testing.json
            config.AddJsonFile("appsettings.Testing.json");
        });
        
        builder.ConfigureServices(services =>
        {
            // Substitui connection string do Redis para usar container de teste
            var redisConnectionString = _redisContainer!.GetConnectionString();
            
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
            });
        });
    }
    
    public async Task DisposeAsync()
    {
        if (_redisContainer != null)
        {
            await _redisContainer.DisposeAsync();
        }
    }
}
```

### 2. Teste de Endpoint GET

```csharp
// ProductsControllerTests.cs
public class ProductsControllerTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly HttpClient _client;
    private readonly WebApplicationFactoryFixture _factory;
    
    public ProductsControllerTests(WebApplicationFactoryFixture factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task GET_Products_ReturnsSuccessAndProducts()
    {
        // Act - Requisição HTTP REAL
        var response = await _client.GetAsync("/api/v1/products?page=1&size=10");
        
        // Assert - Status code
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Assert - Content type
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
        
        // Assert - Deserialização JSON
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        
        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.TotalCount.Should().BeGreaterThan(0);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
        
        // Assert - Estrutura do produto
        var firstProduct = result.Items.First();
        firstProduct.Id.Should().BeGreaterThan(0);
        firstProduct.Name.Should().NotBeNullOrEmpty();
        firstProduct.Price.Should().BeGreaterThan(0);
    }
    
    [Fact]
    public async Task GET_ProductById_ReturnsProduct()
    {
        // Arrange - Primeiro pega lista para ter um ID válido
        var listResponse = await _client.GetAsync("/api/v1/products?page=1&size=1");
        var products = await listResponse.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        var productId = products!.Items.First().Id;
        
        // Act
        var response = await _client.GetAsync($"/api/v1/products/{productId}");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        product.Should().NotBeNull();
        product!.Id.Should().Be(productId);
    }
    
    [Fact]
    public async Task GET_ProductById_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/products/99999");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Message.Should().Contain("not found");
    }
}
```

### 3. Teste de Cache com Redis

```csharp
// CacheIntegrationTests.cs
public class CacheIntegrationTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly HttpClient _client;
    
    public CacheIntegrationTests(WebApplicationFactoryFixture factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task GET_Products_SecondCall_ShouldUseCachedData()
    {
        // Act 1 - Primeira chamada (cache miss)
        var stopwatch1 = Stopwatch.StartNew();
        var response1 = await _client.GetAsync("/api/v1/products?page=1&size=10");
        stopwatch1.Stop();
        
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstCallTime = stopwatch1.ElapsedMilliseconds;
        
        // Act 2 - Segunda chamada (cache hit)
        var stopwatch2 = Stopwatch.StartNew();
        var response2 = await _client.GetAsync("/api/v1/products?page=1&size=10");
        stopwatch2.Stop();
        
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondCallTime = stopwatch2.ElapsedMilliseconds;
        
        // Assert - Segunda chamada deve ser mais rápida (cache hit)
        secondCallTime.Should().BeLessThan(firstCallTime);
        
        // Assert - Dados devem ser idênticos
        var data1 = await response1.Content.ReadAsStringAsync();
        var data2 = await response2.Content.ReadAsStringAsync();
        data1.Should().Be(data2);
    }
    
    [Fact]
    public async Task POST_Product_ShouldInvalidateCache()
    {
        // Arrange - Popula cache
        await _client.GetAsync("/api/v1/products?page=1&size=10");
        
        // Act - Cria novo produto (deve invalidar cache)
        var newProduct = new CreateProductDto
        {
            Name = "Test Product",
            Brand = "Test Brand",
            Price = 999.99m,
            StockQuantity = 10,
            Rating = 4.5m
        };
        
        var createResponse = await _client.PostAsJsonAsync("/api/v1/products", newProduct);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        // Assert - Próxima chamada deve ter o novo produto (cache foi invalidado)
        var listResponse = await _client.GetAsync("/api/v1/products?page=1&size=100");
        var products = await listResponse.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        
        products!.Items.Should().Contain(p => p.Name == "Test Product");
    }
}
```

### 4. Teste de Health Checks

```csharp
// HealthChecksIntegrationTests.cs
public class HealthChecksIntegrationTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly HttpClient _client;
    
    public HealthChecksIntegrationTests(WebApplicationFactoryFixture factory)
    {
        _client = factory.CreateClient();
    }
    
    [Fact]
    public async Task GET_Health_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health");
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }
    
    [Fact]
    public async Task GET_HealthReady_WithRedis_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health/ready");
        
        // Assert - Deve validar Redis
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<HealthCheckResult>();
        result!.Status.Should().Be("Healthy");
        result.Entries.Should().ContainKey("redis");
        result.Entries["redis"].Status.Should().Be("Healthy");
    }
}
```

### 5. Teste de Rate Limiting

```csharp
// RateLimitingIntegrationTests.cs
public class RateLimitingIntegrationTests : IClassFixture<WebApplicationFactoryFixture>
{
    private readonly WebApplicationFactoryFixture _factory;
    
    public RateLimitingIntegrationTests(WebApplicationFactoryFixture factory)
    {
        _factory = factory;
    }
    
    [Fact]
    public async Task GET_Products_ExceedingRateLimit_Returns429()
    {
        // Arrange - Cria cliente isolado para este teste
        var client = _factory.CreateClient();
        
        // Act - Faz 101 requisições rapidamente
        var tasks = Enumerable.Range(0, 101)
            .Select(_ => client.GetAsync("/api/v1/products"))
            .ToList();
        
        var responses = await Task.WhenAll(tasks);
        
        // Assert - Pelo menos uma deve retornar 429 (Too Many Requests)
        responses.Should().Contain(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        
        // Assert - Header Retry-After deve estar presente
        var rateLimitedResponse = responses.First(r => r.StatusCode == HttpStatusCode.TooManyRequests);
        rateLimitedResponse.Headers.Should().Contain(h => h.Key == "Retry-After");
    }
}
```

---

## 🎯 Comparação: Unitário vs Integração

### Seu Teste Unitário Atual (Mock)

```csharp
// ProductComparison.UnitTests/ProductServiceTests.cs
[Fact]
public async Task GetAllProducts_ShouldReturnProducts()
{
    // Arrange - TUDO É FAKE
    var mockRepo = new Mock<IProductRepository>();
    var mockCache = new Mock<ICacheService>();
    var mockLogger = new Mock<ILogger<ProductService>>();
    
    var fakeProducts = new List<Product> { /* fake data */ };
    mockRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(fakeProducts);
    
    var service = new ProductService(mockRepo.Object, mockCache.Object, mockLogger.Object);
    
    // Act - Testa APENAS a lógica do ProductService
    var result = await service.GetAllProductsAsync(1, 10);
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal(fakeProducts.Count, result.TotalCount);
}
```

**O que NÃO é testado:**
- ❌ CSV existe e pode ser lido?
- ❌ Redis conecta?
- ❌ JSON serializa corretamente?
- ❌ Controller retorna status code correto?
- ❌ Middleware captura exceções?

### Teste de Integração (Real)

```csharp
// ProductComparison.IntegrationTests/ProductsControllerTests.cs
[Fact]
public async Task GET_Products_ReturnsRealDataFromCSV()
{
    // Arrange - API REAL rodando
    var client = _factory.CreateClient();
    
    // Act - HTTP REQUEST REAL
    var response = await client.GetAsync("/api/v1/products?page=1&size=10");
    
    // Assert - TUDO É TESTADO DE VERDADE
    response.StatusCode.Should().Be(HttpStatusCode.OK);  // ✅ Controller funcionou
    
    var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();  // ✅ JSON serializou
    result!.Items.Should().NotBeEmpty();  // ✅ CSV foi lido
    result.TotalCount.Should().BeGreaterThan(0);  // ✅ Dados reais retornados
}
```

**O que É testado:**
- ✅ CSV existe e foi lido pelo ProductRepository
- ✅ Redis conectou e cacheou os dados
- ✅ JSON serializou/deserializou corretamente
- ✅ Controller retornou 200 OK
- ✅ Middleware não quebrou
- ✅ Paginação funcionou
- ✅ DTO tem estrutura correta

---

## 📊 Quando Usar Cada Tipo

| Situação | Teste Unitário | Teste de Integração |
|----------|----------------|---------------------|
| **Lógica complexa de negócio** | ✅ Ideal | ❌ Overkill |
| **Validações e regras** | ✅ Rápido e eficiente | ❌ Muito lento |
| **Edge cases** | ✅ Fácil mockar cenários | ❌ Difícil simular |
| **Integração com DB/Cache** | ❌ Mock não valida real | ✅ Essencial |
| **Endpoints HTTP** | ❌ Não testa controller | ✅ Testa fluxo completo |
| **Configurações** | ❌ Não carrega appsettings | ✅ Valida config real |
| **Performance** | ⚡ ~10ms | 🐌 ~500ms |
| **CI/CD** | ✅ Rodar sempre | ✅ Rodar antes de merge |

### Recomendação de Cobertura

**Pirâmide de Testes (ideal):**
```
        /\
       /  \      ❌ E2E Tests (poucos, críticos)
      /────\     
     /      \    ✅ Integration Tests (médio, fluxos principais)
    /────────\   
   /          \  ✅✅✅ Unit Tests (muitos, toda lógica)
  /────────────\ 
```

**Seu Projeto:**
- ✅ **70% Unit Tests** - Lógica de negócio, validações, edge cases
- ✅ **25% Integration Tests** - Endpoints críticos, cache, DB operations
- ✅ **5% E2E Tests** - Fluxo completo usuario (opcional)

---

## 🚀 Como Adicionar ao Seu Projeto

### 1. Criar projeto de testes

```bash
cd tests
dotnet new xunit -n ProductComparison.IntegrationTests
cd ProductComparison.IntegrationTests

# Adicionar packages
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 9.0.0
dotnet add package Testcontainers.Redis --version 3.10.0
dotnet add package FluentAssertions --version 6.12.1

# Adicionar referência ao projeto Application
dotnet add reference ../../src/ProductComparison.Application/ProductComparison.Application.csproj

# Adicionar ao solution
cd ../..
dotnet sln add tests/ProductComparison.IntegrationTests/ProductComparison.IntegrationTests.csproj
```

### 2. Criar estrutura de arquivos

```bash
cd tests/ProductComparison.IntegrationTests
mkdir Fixtures
mkdir Data
touch Fixtures/WebApplicationFactoryFixture.cs
touch ProductsControllerTests.cs
touch CacheIntegrationTests.cs
```

### 3. Rodar testes

```bash
# Apenas integration tests
dotnet test --filter FullyQualifiedName~IntegrationTests

# Todos os testes (unit + integration)
dotnet test

# Com output detalhado
dotnet test --logger "console;verbosity=detailed"
```

---

## ✅ Checklist de Implementação

### Fase 1: Setup Básico
- [ ] Criar projeto ProductComparison.IntegrationTests
- [ ] Adicionar packages (AspNetCore.Mvc.Testing, xUnit)
- [ ] Criar WebApplicationFactoryFixture
- [ ] Configurar appsettings.Testing.json

### Fase 2: Testes Críticos
- [ ] Testar GET /api/v1/products (lista)
- [ ] Testar GET /api/v1/products/{id} (detalhe)
- [ ] Testar POST /api/v1/products (criar)
- [ ] Testar PUT /api/v1/products/{id} (atualizar)
- [ ] Testar DELETE /api/v1/products/{id} (deletar)

### Fase 3: Testes de Infraestrutura
- [ ] Testar cache hit/miss com Redis
- [ ] Testar invalidação de cache
- [ ] Testar health checks (/health, /health/ready)
- [ ] Testar rate limiting (429 response)

### Fase 4: Testcontainers (Opcional)
- [ ] Adicionar Testcontainers.Redis
- [ ] Substituir Redis mockado por container real
- [ ] Configurar lifecycle (start/stop containers)

### Fase 5: CI/CD
- [ ] Adicionar integration tests no pipeline
- [ ] Configurar Docker-in-Docker (se usar Testcontainers)
- [ ] Gerar relatórios de cobertura

---

## 🎓 Conclusão

**Testes Unitários:**
- 🎯 Testam **lógica de negócio** isolada
- ⚡ Rápidos (milissegundos)
- ✅ Você já tem 14 testes

**Testes de Integração:**
- 🎯 Testam **sistema completo** integrado
- 🐌 Mais lentos (centenas de milissegundos)
- ✅ **Crítico para confiança em produção**

**Para Tech Leads:**
- ❌ Só unit tests = "Lógica está certa, mas pode quebrar"
- ✅ Unit + Integration = "Sistema funciona e está pronto para produção"

---

## 📚 Recursos

- [Microsoft - Integration Tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests)
- [Testcontainers](https://dotnet.testcontainers.org/)
- [WebApplicationFactory](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.mvc.testing.webapplicationfactory-1)
- [FluentAssertions](https://fluentassertions.com/)

---

**Status:** 📝 Guia completo  
**Próximo passo:** Implementar no projeto (opcional mas recomendado para senior level)  
**Tempo estimado:** 2-3 horas  
**Impacto no score:** 8.5/10 → 9.5/10 🚀
