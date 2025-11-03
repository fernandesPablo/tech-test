# 🐳 Docker Setup - Product Comparison API

Guia completo para executar a aplicação Product Comparison API usando Docker.

---

## 📋 Pré-requisitos

- [Docker](https://docs.docker.com/get-docker/) (20.10 ou superior)
- [Docker Compose](https://docs.docker.com/compose/install/) (v2.0 ou superior)
- 2GB de RAM disponível
- Portas livres: 5000 (API), 6379 (Redis)

---

## 🚀 Quick Start

### Opção 1: Script Automatizado (Recomendado)

**Linux/macOS:**
```bash
chmod +x docker-run.sh
./docker-run.sh up
```

**Windows:**
```cmd
docker-run.bat up
```

### Opção 2: Docker Compose Manual

```bash
# Iniciar serviços
docker-compose up -d

# Ver logs
docker-compose logs -f

# Parar serviços
docker-compose down
```

### 🎯 Acessar a Aplicação

Após o startup (aguarde ~30 segundos):

- **API Base:** http://localhost:5000/api/v1/products
- **Swagger UI:** http://localhost:5000/swagger
- **Health Check:** http://localhost:5000/health
- **Redis:** localhost:6379

---

## 📁 Arquitetura Docker

### Containers

```
┌─────────────────────────────────────────┐
│  productcomparison-api                  │
│  - .NET 9.0 Runtime                     │
│  - Port: 5000 → 8080                    │
│  - Volumes: CSV, Logs                   │
│  - Health: /health/live                 │
└─────────────────────────────────────────┘
              ↓ depends_on
┌─────────────────────────────────────────┐
│  productcomparison-redis                │
│  - Redis 7 Alpine                       │
│  - Port: 6379                           │
│  - Volume: redis-data (persistent)      │
│  - Health: redis-cli ping               │
└─────────────────────────────────────────┘
```

### Volumes Persistentes

| Volume | Descrição | Path no Container |
|--------|-----------|-------------------|
| `redis-data` | Dados do Redis | `/data` |
| `./src/.../Csv` | Arquivo CSV dos produtos | `/app/ProductComparison.Application/Csv` |
| `./logs` | Logs da aplicação | `/app/logs` |

---

## 🛠️ Comandos Disponíveis

### Scripts Helper

| Comando | Descrição |
|---------|-----------|
| `./docker-run.sh up` | Inicia todos os serviços |
| `./docker-run.sh down` | Para todos os serviços |
| `./docker-run.sh restart` | Reinicia serviços |
| `./docker-run.sh logs` | Mostra logs de todos os serviços |
| `./docker-run.sh logs-api` | Logs apenas da API |
| `./docker-run.sh logs-redis` | Logs apenas do Redis |
| `./docker-run.sh build` | Rebuild das imagens |
| `./docker-run.sh clean` | Remove tudo (containers, volumes, imagens) |
| `./docker-run.sh dev` | Modo desenvolvimento (hot reload) |
| `./docker-run.sh prod` | Modo produção |
| `./docker-run.sh health` | Verifica saúde dos serviços |
| `./docker-run.sh test` | Executa testes unitários |

### Docker Compose Direto

```bash
# Iniciar em background
docker-compose up -d

# Iniciar com build forçado
docker-compose up -d --build

# Ver logs em tempo real
docker-compose logs -f

# Ver logs apenas da API
docker-compose logs -f api

# Parar serviços (mantém volumes)
docker-compose down

# Parar e remover volumes
docker-compose down -v

# Rebuild das imagens
docker-compose build --no-cache

# Escalar API (múltiplas instâncias)
docker-compose up -d --scale api=3

# Executar comando dentro do container
docker-compose exec api bash

# Ver status dos serviços
docker-compose ps

# Ver uso de recursos
docker stats
```

---

## 🔧 Modos de Execução

### 1. Desenvolvimento (`docker-compose.dev.yml`)

**Características:**
- Hot reload (dotnet watch)
- Volume mapping do código fonte
- Redis local incluso
- Swagger habilitado
- Logs verbosos

**Uso:**
```bash
docker-compose -f docker-compose.dev.yml up
```

**Vantagens:**
- ✅ Mudanças no código refletem automaticamente
- ✅ Debug facilitado
- ✅ Não precisa rebuild constante

### 2. Produção (`docker-compose.yml`)

**Características:**
- Build otimizado (multi-stage)
- API + Redis integrados
- Health checks configurados
- Restart automático
- Volumes persistentes

**Uso:**
```bash
docker-compose up -d --build
```

**Vantagens:**
- ✅ Imagem otimizada (~200MB)
- ✅ Production-ready
- ✅ Auto-recovery

### 3. Produção Externa (`docker-compose.prod.yml`)

**Características:**
- Apenas API (Redis externo)
- Resource limits configurados
- Variáveis de ambiente customizáveis

**Uso:**
```bash
export REDIS_CONNECTION="prod-redis.example.com:6379"
docker-compose -f docker-compose.prod.yml up -d
```

---

## ⚙️ Variáveis de Ambiente

Configuráveis no `docker-compose.yml`:

| Variável | Descrição | Padrão |
|----------|-----------|--------|
| `ASPNETCORE_ENVIRONMENT` | Ambiente da aplicação | `Production` |
| `ASPNETCORE_URLS` | URLs de binding | `http://+:8080` |
| `ConnectionStrings__RedisConnection` | Connection string do Redis | `redis:6379` |
| `RepositoryConfiguration__BaseDirectory` | Path base do CSV | `/app/ProductComparison.Application` |
| `RepositoryConfiguration__CsvFolder` | Pasta do CSV | `Csv` |
| `RepositoryConfiguration__ProductsFileName` | Nome do arquivo CSV | `products.csv` |

**Exemplo de override:**
```yaml
environment:
  - ConnectionStrings__RedisConnection=my-redis-host:6379
  - ASPNETCORE_ENVIRONMENT=Staging
```

---

## 🏥 Health Checks

### Container Health

```bash
# Verificar saúde do Redis
docker-compose exec redis redis-cli ping
# Resposta esperada: PONG

# Verificar saúde da API
curl http://localhost:5000/health/live
# Resposta esperada: {"status":"Healthy"}

# Health check detalhado
curl http://localhost:5000/health | jq
```

### Endpoints de Health

| Endpoint | Descrição | Uso Kubernetes |
|----------|-----------|----------------|
| `/health` | Health check completo com métricas | - |
| `/health/ready` | Readiness probe (inclui Redis) | `readinessProbe` |
| `/health/live` | Liveness probe (apenas API) | `livenessProbe` |

---

## 📊 Monitoramento e Logs

### Ver Logs

```bash
# Todos os serviços
docker-compose logs -f

# Apenas API
docker-compose logs -f api

# Últimas 100 linhas
docker-compose logs --tail=100 api

# Filtrar por erro
docker-compose logs api | grep ERROR
```

### Logs Estruturados (Serilog)

Formato: `[Timestamp Level] Message {Properties}`

```json
[15:30:45 INF] Received request to get products page 1 with size 10. CorrelationId: 00-abc123 {"CorrelationId":"00-abc123","Endpoint":"GET /api/v1/products"}
```

### Volumes de Logs

Logs persistentes em: `./logs/product-comparison-{date}.log`

---

## 🔍 Troubleshooting

### Problema: Porta 5000 já em uso

**Solução 1:** Mudar porta no `docker-compose.yml`
```yaml
ports:
  - "8080:8080"  # Ao invés de 5000:8080
```

**Solução 2:** Parar serviço que está usando a porta
```bash
# Linux/macOS
lsof -ti:5000 | xargs kill -9

# Windows
netstat -ano | findstr :5000
taskkill /PID <PID> /F
```

### Problema: Redis não conecta

**Verificar:**
```bash
# Redis está rodando?
docker-compose ps redis

# Redis está saudável?
docker-compose exec redis redis-cli ping

# Logs do Redis
docker-compose logs redis
```

**Solução:**
```bash
# Restart apenas do Redis
docker-compose restart redis

# Rebuild completo
docker-compose down -v
docker-compose up -d --build
```

### Problema: CSV file not found

**Verificar:**
```bash
# Arquivo existe no host?
ls -la src/ProductComparison.Application/Csv/products.csv

# Arquivo existe no container?
docker-compose exec api ls -la /app/ProductComparison.Application/Csv/
```

**Solução:**
```bash
# Recriar volume
docker-compose down -v
docker-compose up -d
```

### Problema: Out of Memory

**Solução:** Aumentar limites no `docker-compose.yml`
```yaml
deploy:
  resources:
    limits:
      memory: 1G  # Ao invés de 512M
```

### Problema: Build falha

**Verificar:**
```bash
# Espaço em disco
df -h

# Limpar cache do Docker
docker system prune -a
```

**Rebuild limpo:**
```bash
docker-compose down -v --rmi all
docker-compose build --no-cache
docker-compose up -d
```

---

## 🚀 Deploy em Produção

### Docker Swarm

```bash
# Inicializar Swarm
docker swarm init

# Deploy do stack
docker stack deploy -c docker-compose.yml productcomparison

# Listar serviços
docker stack services productcomparison

# Escalar API
docker service scale productcomparison_api=3

# Ver logs
docker service logs -f productcomparison_api
```

### Kubernetes

Converter com Kompose:
```bash
# Instalar Kompose
curl -L https://github.com/kubernetes/kompose/releases/download/v1.31.2/kompose-linux-amd64 -o kompose

# Converter
kompose convert -f docker-compose.yml

# Aplicar no Kubernetes
kubectl apply -f .
```

---

## 🔐 Segurança

### Boas Práticas Implementadas

- ✅ Imagem base oficial Microsoft
- ✅ Multi-stage build (imagem menor)
- ✅ Non-root user no container
- ✅ Health checks configurados
- ✅ Resource limits definidos
- ✅ Secrets via environment variables
- ✅ Network isolation (bridge network)

### Recomendações Adicionais

```yaml
# Adicionar secrets do Docker
docker secret create redis_password redis_pass.txt

# Usar secrets no compose (Swarm mode)
services:
  redis:
    secrets:
      - redis_password
    command: redis-server --requirepass /run/secrets/redis_password
```

---

## 📦 Build Sizes

| Imagem | Tamanho | Descrição |
|--------|---------|-----------|
| `mcr.microsoft.com/dotnet/sdk:9.0` | ~800MB | Build stage (não inclusa na imagem final) |
| `mcr.microsoft.com/dotnet/aspnet:9.0` | ~220MB | Runtime base |
| `productcomparison-api` | ~240MB | Imagem final (runtime + app) |
| `redis:7-alpine` | ~30MB | Redis Alpine |

**Total em disco:** ~270MB (API + Redis)

---

## 🧪 Testes

### Rodar Testes no Container

```bash
# Via script
./docker-run.sh test

# Docker Compose direto
docker-compose exec api dotnet test /src/tests/ProductComparison.UnitTests/ProductComparison.UnitTests.csproj

# Com coverage
docker-compose exec api dotnet test --collect:"XPlat Code Coverage"
```

### Teste de Carga

```bash
# Apache Bench (100 requisições, 10 concorrentes)
ab -n 100 -c 10 http://localhost:5000/api/v1/products

# Fortio
fortio load -qps 100 -t 30s http://localhost:5000/api/v1/products
```

---

## 📚 Referências

- [Dockerfile Best Practices](https://docs.docker.com/develop/develop-images/dockerfile_best-practices/)
- [Docker Compose Reference](https://docs.docker.com/compose/compose-file/)
- [.NET Docker Images](https://hub.docker.com/_/microsoft-dotnet)
- [Redis Docker Hub](https://hub.docker.com/_/redis)

---

## ✅ Checklist de Deploy

- [ ] Docker e Docker Compose instalados
- [ ] Portas 5000 e 6379 disponíveis
- [ ] Arquivo `products.csv` existe em `src/ProductComparison.Application/Csv/`
- [ ] Variáveis de ambiente configuradas (se necessário)
- [ ] Health checks funcionando
- [ ] Logs sendo gerados corretamente
- [ ] Redis persistindo dados (verificar volume)
- [ ] Rate limiting testado
- [ ] Backup do CSV configurado

---

**Status:** ✅ Production Ready  
**Última atualização:** 02/11/2025
