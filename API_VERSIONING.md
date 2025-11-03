# API Versioning - Documentação

## 🔢 Implementação de Versionamento

### Estratégia: URL Path Versioning

**Padrão adotado:** `/api/v{version}/resource`

```
❌ Antes: /api/products
✅ Agora:  /api/v1/products
```

---

## 📍 Endpoints Versionados

### v1 (atual)

| Método | Endpoint | Descrição |
|--------|----------|-----------|
| GET | `/api/v1/products` | Lista produtos com paginação |
| GET | `/api/v1/products/{id}` | Busca produto por ID |
| GET | `/api/v1/products/compare?ids={ids}` | Compara múltiplos produtos |
| POST | `/api/v1/products` | Cria novo produto |
| PUT | `/api/v1/products/{id}` | Atualiza produto existente |
| DELETE | `/api/v1/products/{id}` | Remove produto |

---

## 🛠️ Implementação Técnica

### Controller com Versionamento

```csharp
[ApiController]
[Route("api/v1/[controller]")]  // Versão fixa no route
[Produces("application/json")]
public class ProductsController : ControllerBase
{
    // ...
}
```

### Alternativa: Versionamento Dinâmico (não implementado)

Para versionamento mais sofisticado, use o pacote `Asp.Versioning.Mvc`:

```csharp
// Install-Package Asp.Versioning.Http

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class ProductsController : ControllerBase { }
```

---

## 🚀 Estratégias de Versionamento

### 1. URL Path (Implementada) ✅

**Formato:** `/api/v1/products`

**Vantagens:**
- ✅ Explícito e visível
- ✅ Fácil de testar (Postman, curl)
- ✅ Cache-friendly (URLs diferentes)
- ✅ Simples de documentar no Swagger

**Desvantagens:**
- ❌ URLs mudam entre versões
- ❌ Pode quebrar bookmarks/links

**Quando usar:** APIs públicas, REST puro

### 2. Query String

**Formato:** `/api/products?api-version=1.0`

**Vantagens:**
- ✅ URL base permanece a mesma
- ✅ Fácil de adicionar

**Desvantagens:**
- ❌ Menos visível
- ❌ Pode ser ignorado

### 3. Header

**Formato:** `X-API-Version: 1` ou `Accept: application/vnd.api.v1+json`

**Vantagens:**
- ✅ URL limpa
- ✅ Segue REST puro (Content Negotiation)

**Desvantagens:**
- ❌ Menos óbvio
- ❌ Difícil de testar manualmente

### 4. Media Type (Content Negotiation)

**Formato:** `Accept: application/vnd.myapi.v1+json`

**Vantagens:**
- ✅ REST hipermídia puro
- ✅ Múltiplos formatos por versão

**Desvantagens:**
- ❌ Mais complexo
- ❌ Menos adotado

---

## 📋 Boas Práticas

### 1. **Nunca Remova Versões Antigas Imediatamente**

```
✅ Depreciation timeline:
- v1 lançada: 01/01/2025
- v2 lançada: 01/06/2025
- v1 deprecated: 01/06/2025 (warning nos responses)
- v1 sunset: 01/12/2025 (6 meses de transição)
```

### 2. **Comunique Breaking Changes**

```json
// Response headers da v1 após lançamento da v2
{
  "Warning": "299 - \"Deprecated API. Please migrate to v2. Sunset: 2025-12-01\"",
  "Sunset": "Sun, 01 Dec 2025 00:00:00 GMT",
  "Link": "</api/v2/products>; rel=\"successor-version\""
}
```

### 3. **Documente Diferenças Entre Versões**

```markdown
## v2 Breaking Changes (vs v1)
- ❌ Removed: `Rating` property (moved to nested `Reviews` object)
- ✅ Added: `Reviews.averageRating` and `Reviews.count`
- ⚠️ Changed: `Price` now includes `currency` field (was optional, now required)
```

### 4. **Versionamento Semântico**

```
v1.0 → v1.1  (backward compatible, new features)
v1.1 → v2.0  (breaking changes)
```

---

## 🔄 Roadmap de Versões

### v1 (atual)
- ✅ CRUD básico de produtos
- ✅ Comparação de produtos
- ✅ Paginação
- ✅ Cache Redis
- ✅ Rate limiting

### v2 (futuro - exemplo)
**Breaking changes potenciais:**
- 📦 Adicionar suporte a múltiplas moedas (`price.amount` + `price.currency`)
- 📦 Nested reviews (`reviews: { averageRating, count, items: [...] }`)
- 📦 HATEOAS links (`_links`, `_embedded`)
- 📦 GraphQL endpoint alternativo
- 📦 WebSocket para notificações

---

## 🧪 Testando Versionamento

### cURL
```bash
# v1
curl http://localhost:5000/api/v1/products

# v2 (futuro)
curl http://localhost:5000/api/v2/products
```

### REST Client (VS Code)
```http
### v1
GET http://localhost:5000/api/v1/products/1
Accept: application/json

### v2 (futuro)
GET http://localhost:5000/api/v2/products/1
Accept: application/json
```

---

## 📊 Monitoramento de Versões

### Logging por Versão

```csharp
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value;
    var version = path?.Contains("/v1/") ? "v1" :
                  path?.Contains("/v2/") ? "v2" : "unknown";
    
    Log.Information("API Version: {ApiVersion}, Path: {Path}", version, path);
    await next();
});
```

### Métricas (Prometheus/OpenTelemetry)

```csharp
// Track usage per version
var versionCounter = Metrics.CreateCounter(
    "api_requests_by_version",
    "Number of requests per API version",
    new CounterConfiguration { LabelNames = new[] { "version", "endpoint" } }
);

versionCounter.WithLabels(version, endpoint).Inc();
```

---

## ⚙️ Configuração no Swagger

### Swagger UI com Múltiplas Versões

```csharp
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "Product Comparison API v1", 
        Version = "v1",
        Description = "First version with basic CRUD"
    });
    
    c.SwaggerDoc("v2", new OpenApiInfo 
    { 
        Title = "Product Comparison API v2", 
        Version = "v2",
        Description = "Enhanced version with reviews and multi-currency"
    });
});

app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1");
    c.SwaggerEndpoint("/swagger/v2/swagger.json", "API v2");
});
```

---

## 🎯 Checklist de Versionamento

- [x] Versão v1 definida na rota (`/api/v1/`)
- [x] Todos os endpoints atualizados
- [x] Logs refletem versão nos endpoints
- [x] Documentação Swagger atualizada
- [x] Arquivo .http atualizado
- [ ] Política de deprecation definida
- [ ] Headers de sunset configurados (quando houver v2)
- [ ] Monitoramento por versão (métricas)
- [ ] Changelog de versões documentado

---

## 📚 Referências

- [Microsoft API Versioning Best Practices](https://learn.microsoft.com/en-us/azure/architecture/best-practices/api-design#versioning-a-restful-web-api)
- [Semantic Versioning](https://semver.org/)
- [RFC 8594 - Sunset HTTP Header](https://datatracker.ietf.org/doc/html/rfc8594)
- [API Versioning Package](https://github.com/dotnet/aspnet-api-versioning)

---

**Implementado em:** 02/11/2025  
**Versão atual:** v1  
**Status:** ✅ Pronto para futuras versões
