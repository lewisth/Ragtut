# RAG System Setup Script for Windows
# This script automates the initial setup of the RAG system

param(
    [switch]$SkipOllama,
    [string]$OllamaModel = "llama2"
)

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "RAG System Setup Script" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Check if .NET 9 is installed
Write-Host "Checking .NET installation..." -ForegroundColor Yellow
try {
    $dotnetVersion = dotnet --version
    Write-Host "✓ .NET version: $dotnetVersion" -ForegroundColor Green
    
    if (-not $dotnetVersion.StartsWith("9.")) {
        Write-Warning "⚠ .NET 9 is recommended. Current version: $dotnetVersion"
        Write-Host "Download .NET 9 from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
    }
} catch {
    Write-Host "✗ .NET is not installed or not in PATH" -ForegroundColor Red
    Write-Host "Please install .NET 9 SDK from: https://dotnet.microsoft.com/download/dotnet/9.0" -ForegroundColor Yellow
    exit 1
}

# Create required directories
Write-Host "Creating required directories..." -ForegroundColor Yellow
$directories = @("data", "logs", "temp", "models", "config")
foreach ($dir in $directories) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "✓ Created directory: $dir" -ForegroundColor Green
    } else {
        Write-Host "✓ Directory exists: $dir" -ForegroundColor Green
    }
}

# Restore dependencies
Write-Host "Restoring .NET dependencies..." -ForegroundColor Yellow
try {
    dotnet restore
    Write-Host "✓ Dependencies restored successfully" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to restore dependencies" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

# Build the solution
Write-Host "Building the solution..." -ForegroundColor Yellow
try {
    dotnet build --configuration Release
    Write-Host "✓ Solution built successfully" -ForegroundColor Green
} catch {
    Write-Host "✗ Failed to build solution" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

# Copy configuration templates
Write-Host "Setting up configuration files..." -ForegroundColor Yellow
$configFiles = @(
    @{Source = "Ragtut.Core\appsettings.template.json"; Target = "VectorIndexer\appsettings.json"},
    @{Source = "Ragtut.Core\appsettings.template.json"; Target = "RagChat\appsettings.json"}
)

foreach ($config in $configFiles) {
    if (Test-Path $config.Source) {
        if (-not (Test-Path $config.Target)) {
            Copy-Item $config.Source $config.Target
            Write-Host "✓ Created configuration: $($config.Target)" -ForegroundColor Green
        } else {
            Write-Host "✓ Configuration exists: $($config.Target)" -ForegroundColor Green
        }
    } else {
        Write-Host "⚠ Template not found: $($config.Source)" -ForegroundColor Yellow
    }
}

# Check for Ollama installation
if (-not $SkipOllama) {
    Write-Host "Checking Ollama installation..." -ForegroundColor Yellow
    try {
        $ollamaVersion = ollama --version
        Write-Host "✓ Ollama is installed: $ollamaVersion" -ForegroundColor Green
        
        # Test Ollama service
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:11434" -TimeoutSec 5 -ErrorAction Stop
            Write-Host "✓ Ollama service is running" -ForegroundColor Green
            
            # Pull the specified model
            Write-Host "Pulling Ollama model: $OllamaModel..." -ForegroundColor Yellow
            Write-Host "This may take several minutes..." -ForegroundColor Yellow
            & ollama pull $OllamaModel
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✓ Model '$OllamaModel' pulled successfully" -ForegroundColor Green
            } else {
                Write-Host "⚠ Failed to pull model '$OllamaModel'" -ForegroundColor Yellow
                Write-Host "You can manually pull it later with: ollama pull $OllamaModel" -ForegroundColor Yellow
            }
            
        } catch {
            Write-Host "⚠ Ollama service is not running" -ForegroundColor Yellow
            Write-Host "Please start Ollama service or restart your computer" -ForegroundColor Yellow
        }
        
    } catch {
        Write-Host "⚠ Ollama is not installed" -ForegroundColor Yellow
        Write-Host "Please install Ollama from: https://ollama.ai" -ForegroundColor Yellow
        Write-Host "After installation, run: ollama pull $OllamaModel" -ForegroundColor Yellow
    }
}

# Check for sample documents
Write-Host "Checking sample documents..." -ForegroundColor Yellow
if (Test-Path "documents") {
    $docCount = (Get-ChildItem -Path "documents" -File).Count
    if ($docCount -gt 0) {
        Write-Host "✓ Found $docCount sample documents in 'documents' folder" -ForegroundColor Green
    } else {
        Write-Host "⚠ No documents found in 'documents' folder" -ForegroundColor Yellow
        Write-Host "Add PDF, TXT, DOCX, or MD files to the 'documents' folder for testing" -ForegroundColor Yellow
    }
} else {
    Write-Host "⚠ Documents folder not found" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Setup Complete!" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Next Steps:" -ForegroundColor White
Write-Host "1. Add documents to the 'documents' folder" -ForegroundColor White
Write-Host "2. Index documents:" -ForegroundColor White
Write-Host "   cd VectorIndexer" -ForegroundColor Gray
Write-Host "   dotnet run" -ForegroundColor Gray
Write-Host "3. Start the chat interface:" -ForegroundColor White
Write-Host "   cd RagChat" -ForegroundColor Gray
Write-Host "   dotnet run" -ForegroundColor Gray
Write-Host ""

if ($SkipOllama) {
    Write-Host "Note: Ollama setup was skipped. Make sure to install and configure Ollama manually." -ForegroundColor Yellow
}

Write-Host "For detailed instructions, see DEPLOYMENT-GUIDE.md" -ForegroundColor Cyan 