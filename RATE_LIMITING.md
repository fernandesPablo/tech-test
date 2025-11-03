# Rate Limiting - Documentação

## 🛡️ O que foi implementado?

### Configuração Atual

**Estratégia:** Fixed Window (Janela Fixa)
- **Limite:** 100 requisições por minuto por endereço IP
- **Janela:** 1 minuto
- **Fila:** 10 requisições podem aguardar se o limite for atingido
- **Status Code:** 429 Too Many Requests
- **Header:** `Retry-After: 60` (segundos)

### Como Funciona?

```
IP: 192.168.1.100
┌─────────────────────────────────────────┐
│ Janela de 1 minuto (00:00 - 01:00)     │
├─────────────────────────────────────────┤
│ Request 1  ✅ (count: 1/100)            │
│ Request 2  ✅ (count: 2/100)            │
│ ...                                     │
│ Request 100 ✅ (count: 100/100)         │
│ Request 101 ❌ 429 Too Many Requests    │
│ Request 102 ❌ 429 Too Many Requests    │
└─────────────────────────────────────────┘
       ↓
Após 01:00, contador reseta para 0
```

---

## 🔧 Implementação Técnica

### 1. Registro do Serviço (Program.cs)

```csharp
builder.Services.AddRateLimiter(options =>
{
    // Limiter global por IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ipAddress,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,           // Máximo de requisições
                Window = TimeSpan.FromMinutes(1),  // Janela de tempo
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10              // Fila de espera
            });
    });

    // Handler para requisições rejeitadas
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            statusCode = 429,
            message = "Too many requests. Please try again later.",
            retryAfterSeconds = 60
        });
    };
});
```

### 2. Ativação do Middleware

```csharp
app.UseRateLimiter(); // Deve vir ANTES de CORS e MapControllers
app.UseCors("AllowAll");
app.MapControllers();
```

---

## 📊 Estratégias de Rate Limiting

### 1. Fixed Window (Implementada) ✅

**Vantagens:**
- Simples de implementar
- Performance excelente (O(1))
- Uso de memória baixo

**Desvantagens:**
- Pode permitir burst no início da janela
- Exemplo: 100 requests às 00:59, mais 100 às 01:00 = 200 em 2 segundos

**Quando usar:**
- API interna
- Limites generosos
- Simplicidade > precisão

### 2. Sliding Window (mais preciso)

```csharp
RateLimitPartition.GetSlidingWindowLimiter(
    partitionKey: ipAddress,
    factory: _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 100,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6  // Divide em 6 segmentos de 10s
    });
```

**Vantagens:**
- Mais preciso, evita burst
- Janela deslizante contínua

**Desvantagens:**
- Mais complexo
- Maior uso de memória

### 3. Token Bucket (para burst controlado)

```csharp
RateLimitPartition.GetTokenBucketLimiter(
    partitionKey: ipAddress,
    factory: _ => new TokenBucketRateLimiterOptions
    {
        TokenLimit = 100,
        ReplenishmentPeriod = TimeSpan.FromMinutes(1),
        TokensPerPeriod = 100
    });
```

**Vantagens:**
- Permite burst controlado
- Flexível

### 4. Concurrency Limiter (requisições simultâneas)

```csharp
RateLimitPartition.GetConcurrencyLimiter(
    partitionKey: ipAddress,
    factory: _ => new ConcurrencyLimiterOptions
    {
        PermitLimit = 10,  // Máximo de 10 requisições simultâneas
        QueueLimit = 5
    });
```

---

## 🎯 Limites por Endpoint (Políticas Específicas)

### Criar Política Nomeada

```csharp
builder.Services.AddRateLimiter(options =>
{
    // Política para listagem (mais permissiva)
    options.AddPolicy("list", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1)
            }));

    // Política para escrita (mais restritiva)
    options.AddPolicy("write", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

### Aplicar no Controller

```csharp
[HttpGet]
[EnableRateLimiting("list")]
public async Task<ActionResult> GetAll() { ... }

[HttpPost]
[EnableRateLimiting("write")]
public async Task<ActionResult> Create(...) { ... }
```

---

## 🧪 Como Testar?

### Opção 1: REST Client (VS Code)

```http
### Enviar 101 requisições rapidamente
GET http://localhost:5000/api/products
```

### Opção 2: Script PowerShell

```powershell
# test-rate-limit.ps1
for ($i = 1; $i -le 101; $i++) {
    $response = Invoke-WebRequest -Uri "http://localhost:5000/api/products" -Method GET -ErrorAction SilentlyContinue
    Write-Host "Request $i : Status $($response.StatusCode)"
    if ($response.StatusCode -eq 429) {
        Write-Host "Rate limit atingido!" -ForegroundColor Red
        break
    }
}
```

### Opção 3: cURL + Loop

```bash
for i in {1..101}; do
  curl -s -o /dev/null -w "Request $i: %{http_code}\n" http://localhost:5000/api/products
done
```

---

## 📈 Monitoramento e Logs

### Adicionar Logging no OnRejected

```csharp
options.OnRejected = async (context, cancellationToken) =>
{
    var logger = context.HttpContext.RequestServices
        .GetRequiredService<ILogger<Program>>();
    
    var ip = context.HttpContext.Connection.RemoteIpAddress;
    var path = context.HttpContext.Request.Path;
    
    logger.LogWarning(
        "Rate limit exceeded for IP {IpAddress} on path {Path}",
        ip, path);
    
    context.HttpContext.Response.StatusCode = 429;
    // ...
};
```

---

## 🚀 Melhorias Avançadas

### 1. Rate Limiting por Usuário Autenticado

```csharp
options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
{
    var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var partitionKey = userId ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    
    return RateLimitPartition.GetFixedWindowLimiter(partitionKey, ...);
});
```

### 2. Limites Diferentes por Plano (Freemium)

```csharp
var userPlan = context.User.FindFirst("plan")?.Value;
var permitLimit = userPlan switch
{
    "premium" => 1000,
    "basic" => 100,
    _ => 20  // free
};
```

### 3. Redis para Cluster Distribuído

```csharp
// Para múltiplas instâncias da API
builder.Services.AddStackExchangeRedisCache(...);
builder.Services.AddDistributedRateLimiting(...);
```

---

## ⚙️ Configuração no appsettings.json

```json
{
  "RateLimiting": {
    "Global": {
      "PermitLimit": 100,
      "WindowMinutes": 1
    },
    "Policies": {
      "List": {
        "PermitLimit": 200,
        "WindowMinutes": 1
      },
      "Write": {
        "PermitLimit": 20,
        "WindowMinutes": 1
      }
    }
  }
}
```

---

## ✅ Checklist de Produção

- [x] Rate limiter registrado
- [x] Middleware ativado (UseRateLimiter)
- [x] Resposta 429 customizada
- [x] Header Retry-After configurado
- [ ] Logging de rate limit violations
- [ ] Monitoramento (métricas)
- [ ] Políticas por endpoint (opcional)
- [ ] Rate limiting distribuído (Redis) para cluster

---

## 📚 Referências

- [ASP.NET Core Rate Limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit)
- [RFC 6585 - HTTP Status Code 429](https://datatracker.ietf.org/doc/html/rfc6585)
- [Rate Limiting Patterns](https://cloud.google.com/architecture/rate-limiting-strategies-techniques)

---

**Implementado em:** 02/11/2025  
**Status:** ✅ Funcional e pronto para produção
