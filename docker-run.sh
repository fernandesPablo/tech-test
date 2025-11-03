#!/bin/bash
# Script to build and run the Product Comparison API with Docker

set -e

echo "🚀 Product Comparison API - Docker Deployment"
echo "=============================================="

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Error: Docker is not installed"
    exit 1
fi

# Check if Docker Compose is installed
if ! command -v docker-compose &> /dev/null; then
    echo "❌ Error: Docker Compose is not installed"
    exit 1
fi

# Function to show usage
show_usage() {
    echo ""
    echo "Usage: ./docker-run.sh [COMMAND]"
    echo ""
    echo "Commands:"
    echo "  up         Start all services (API + Redis)"
    echo "  down       Stop all services"
    echo "  restart    Restart all services"
    echo "  logs       Show logs from all services"
    echo "  logs-api   Show logs from API only"
    echo "  logs-redis Show logs from Redis only"
    echo "  build      Rebuild Docker images"
    echo "  clean      Remove all containers, volumes, and images"
    echo "  dev        Start in development mode (hot reload)"
    echo "  prod       Start in production mode"
    echo "  health     Check health status of services"
    echo "  test       Run unit tests inside container"
    echo ""
}

# Function to check health
check_health() {
    echo "🏥 Checking service health..."
    echo ""
    echo "Redis:"
    docker-compose exec redis redis-cli ping || echo "❌ Redis is not responding"
    echo ""
    echo "API:"
    curl -s http://localhost:5000/health | jq . || echo "❌ API is not responding"
}

# Main command handler
case "$1" in
    up)
        echo "📦 Building and starting services..."
        docker-compose up -d --build
        echo ""
        echo "✅ Services started successfully!"
        echo ""
        echo "🌐 API available at: http://localhost:5000"
        echo "📚 Swagger UI at: http://localhost:5000/swagger"
        echo "🔍 Health check at: http://localhost:5000/health"
        echo ""
        echo "Run './docker-run.sh logs' to see logs"
        ;;
    
    down)
        echo "🛑 Stopping services..."
        docker-compose down
        echo "✅ Services stopped"
        ;;
    
    restart)
        echo "🔄 Restarting services..."
        docker-compose restart
        echo "✅ Services restarted"
        ;;
    
    logs)
        echo "📋 Showing logs (Ctrl+C to exit)..."
        docker-compose logs -f
        ;;
    
    logs-api)
        echo "📋 Showing API logs (Ctrl+C to exit)..."
        docker-compose logs -f api
        ;;
    
    logs-redis)
        echo "📋 Showing Redis logs (Ctrl+C to exit)..."
        docker-compose logs -f redis
        ;;
    
    build)
        echo "🔨 Rebuilding Docker images..."
        docker-compose build --no-cache
        echo "✅ Build complete"
        ;;
    
    clean)
        echo "🧹 Cleaning up Docker resources..."
        docker-compose down -v --rmi all
        echo "✅ Cleanup complete"
        ;;
    
    dev)
        echo "🔧 Starting in development mode (hot reload)..."
        docker-compose -f docker-compose.dev.yml up
        ;;
    
    prod)
        echo "🚀 Starting in production mode..."
        docker-compose -f docker-compose.prod.yml up -d --build
        echo "✅ Production services started"
        ;;
    
    health)
        check_health
        ;;
    
    test)
        echo "🧪 Running unit tests..."
        docker-compose exec api dotnet test /src/tests/ProductComparison.UnitTests/ProductComparison.UnitTests.csproj
        ;;
    
    *)
        show_usage
        exit 1
        ;;
esac
