#!/bin/bash

# Rolling Backend Startup Script
# This script will automatically run database migrations before starting the server

set -e

echo "🚀 Starting Rolling Backend..."
echo ""

# Build the project
echo "📦 Building project..."
dotnet build --configuration Release

# Run the application (migrations will run automatically on startup)
echo ""
echo "▶️  Starting server..."
echo "   - Migrations will run automatically if needed"
echo "   - Server will start after migrations complete"
echo ""

dotnet run --project Rolling.Web --configuration Release
