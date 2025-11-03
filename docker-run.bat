@echo off
REM Script to build and run the Product Comparison API with Docker (Windows)

echo 🚀 Product Comparison API - Docker Deployment
echo ==============================================
echo.

REM Check if Docker is installed
docker --version >nul 2>&1
if errorlevel 1 (
    echo ❌ Error: Docker is not installed
    exit /b 1
)

REM Check if Docker Compose is installed
docker-compose --version >nul 2>&1
if errorlevel 1 (
    echo ❌ Error: Docker Compose is not installed
    exit /b 1
)

if "%1"=="" goto usage

if "%1"=="up" goto up
if "%1"=="down" goto down
if "%1"=="restart" goto restart
if "%1"=="logs" goto logs
if "%1"=="logs-api" goto logs-api
if "%1"=="logs-redis" goto logs-redis
if "%1"=="build" goto build
if "%1"=="clean" goto clean
if "%1"=="dev" goto dev
if "%1"=="prod" goto prod
if "%1"=="health" goto health
if "%1"=="test" goto test
goto usage

:up
echo 📦 Building and starting services...
docker-compose up -d --build
echo.
echo ✅ Services started successfully!
echo.
echo 🌐 API available at: http://localhost:5000
echo 📚 Swagger UI at: http://localhost:5000/swagger
echo 🔍 Health check at: http://localhost:5000/health
echo.
echo Run 'docker-run.bat logs' to see logs
goto end

:down
echo 🛑 Stopping services...
docker-compose down
echo ✅ Services stopped
goto end

:restart
echo 🔄 Restarting services...
docker-compose restart
echo ✅ Services restarted
goto end

:logs
echo 📋 Showing logs (Ctrl+C to exit)...
docker-compose logs -f
goto end

:logs-api
echo 📋 Showing API logs (Ctrl+C to exit)...
docker-compose logs -f api
goto end

:logs-redis
echo 📋 Showing Redis logs (Ctrl+C to exit)...
docker-compose logs -f redis
goto end

:build
echo 🔨 Rebuilding Docker images...
docker-compose build --no-cache
echo ✅ Build complete
goto end

:clean
echo 🧹 Cleaning up Docker resources...
docker-compose down -v --rmi all
echo ✅ Cleanup complete
goto end

:dev
echo 🔧 Starting in development mode (hot reload)...
docker-compose -f docker-compose.dev.yml up
goto end

:prod
echo 🚀 Starting in production mode...
docker-compose -f docker-compose.prod.yml up -d --build
echo ✅ Production services started
goto end

:health
echo 🏥 Checking service health...
echo.
echo Redis:
docker-compose exec redis redis-cli ping
echo.
echo API:
curl -s http://localhost:5000/health
goto end

:test
echo 🧪 Running unit tests...
docker-compose exec api dotnet test /src/tests/ProductComparison.UnitTests/ProductComparison.UnitTests.csproj
goto end

:usage
echo.
echo Usage: docker-run.bat [COMMAND]
echo.
echo Commands:
echo   up         Start all services (API + Redis)
echo   down       Stop all services
echo   restart    Restart all services
echo   logs       Show logs from all services
echo   logs-api   Show logs from API only
echo   logs-redis Show logs from Redis only
echo   build      Rebuild Docker images
echo   clean      Remove all containers, volumes, and images
echo   dev        Start in development mode (hot reload)
echo   prod       Start in production mode
echo   health     Check health status of services
echo   test       Run unit tests inside container
echo.
goto end

:end
