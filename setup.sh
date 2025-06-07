#!/bin/bash

# RAG System Setup Script for macOS and Linux
# This script automates the initial setup of the RAG system

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Default values
SKIP_OLLAMA=false
OLLAMA_MODEL="llama2"

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --skip-ollama)
            SKIP_OLLAMA=true
            shift
            ;;
        --model)
            OLLAMA_MODEL="$2"
            shift 2
            ;;
        -h|--help)
            echo "Usage: $0 [--skip-ollama] [--model MODEL_NAME]"
            echo "  --skip-ollama    Skip Ollama installation and setup"
            echo "  --model          Specify Ollama model to download (default: llama2)"
            exit 0
            ;;
        *)
            echo "Unknown option $1"
            exit 1
            ;;
    esac
done

echo -e "${CYAN}=====================================${NC}"
echo -e "${CYAN}RAG System Setup Script${NC}"
echo -e "${CYAN}=====================================${NC}"
echo ""

# Check if .NET 9 is installed
echo -e "${YELLOW}Checking .NET installation...${NC}"
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    echo -e "${GREEN}✓ .NET version: $DOTNET_VERSION${NC}"
    
    if [[ ! $DOTNET_VERSION == 9.* ]]; then
        echo -e "${YELLOW}⚠ .NET 9 is recommended. Current version: $DOTNET_VERSION${NC}"
        echo -e "${YELLOW}Download .NET 9 from: https://dotnet.microsoft.com/download/dotnet/9.0${NC}"
    fi
else
    echo -e "${RED}✗ .NET is not installed or not in PATH${NC}"
    echo -e "${YELLOW}Please install .NET 9 SDK from: https://dotnet.microsoft.com/download/dotnet/9.0${NC}"
    exit 1
fi

# Create required directories
echo -e "${YELLOW}Creating required directories...${NC}"
DIRECTORIES=("data" "logs" "temp" "models" "config")
for dir in "${DIRECTORIES[@]}"; do
    if [ ! -d "$dir" ]; then
        mkdir -p "$dir"
        echo -e "${GREEN}✓ Created directory: $dir${NC}"
    else
        echo -e "${GREEN}✓ Directory exists: $dir${NC}"
    fi
done

# Restore dependencies
echo -e "${YELLOW}Restoring .NET dependencies...${NC}"
if dotnet restore; then
    echo -e "${GREEN}✓ Dependencies restored successfully${NC}"
else
    echo -e "${RED}✗ Failed to restore dependencies${NC}"
    exit 1
fi

# Build the solution
echo -e "${YELLOW}Building the solution...${NC}"
if dotnet build --configuration Release; then
    echo -e "${GREEN}✓ Solution built successfully${NC}"
else
    echo -e "${RED}✗ Failed to build solution${NC}"
    exit 1
fi

# Copy configuration templates
echo -e "${YELLOW}Setting up configuration files...${NC}"
CONFIG_FILES=(
    "Ragtut.Core/appsettings.template.json:VectorIndexer/appsettings.json"
    "Ragtut.Core/appsettings.template.json:RagChat/appsettings.json"
)

for config in "${CONFIG_FILES[@]}"; do
    IFS=':' read -r source target <<< "$config"
    if [ -f "$source" ]; then
        if [ ! -f "$target" ]; then
            cp "$source" "$target"
            echo -e "${GREEN}✓ Created configuration: $target${NC}"
        else
            echo -e "${GREEN}✓ Configuration exists: $target${NC}"
        fi
    else
        echo -e "${YELLOW}⚠ Template not found: $source${NC}"
    fi
done

# Check for Ollama installation
if [ "$SKIP_OLLAMA" = false ]; then
    echo -e "${YELLOW}Checking Ollama installation...${NC}"
    if command -v ollama &> /dev/null; then
        OLLAMA_VERSION=$(ollama --version 2>/dev/null || echo "unknown")
        echo -e "${GREEN}✓ Ollama is installed: $OLLAMA_VERSION${NC}"
        
        # Test Ollama service
        if curl -s http://localhost:11434 > /dev/null 2>&1; then
            echo -e "${GREEN}✓ Ollama service is running${NC}"
            
            # Pull the specified model
            echo -e "${YELLOW}Pulling Ollama model: $OLLAMA_MODEL...${NC}"
            echo -e "${YELLOW}This may take several minutes...${NC}"
            if ollama pull "$OLLAMA_MODEL"; then
                echo -e "${GREEN}✓ Model '$OLLAMA_MODEL' pulled successfully${NC}"
            else
                echo -e "${YELLOW}⚠ Failed to pull model '$OLLAMA_MODEL'${NC}"
                echo -e "${YELLOW}You can manually pull it later with: ollama pull $OLLAMA_MODEL${NC}"
            fi
        else
            echo -e "${YELLOW}⚠ Ollama service is not running${NC}"
            echo -e "${YELLOW}Please start Ollama service with: ollama serve${NC}"
        fi
    else
        echo -e "${YELLOW}⚠ Ollama is not installed${NC}"
        echo -e "${YELLOW}Installing Ollama...${NC}"
        
        # Install Ollama
        if curl -fsSL https://ollama.ai/install.sh | sh; then
            echo -e "${GREEN}✓ Ollama installed successfully${NC}"
            echo -e "${YELLOW}Please start Ollama service with: ollama serve${NC}"
            echo -e "${YELLOW}Then run: ollama pull $OLLAMA_MODEL${NC}"
        else
            echo -e "${YELLOW}⚠ Failed to install Ollama automatically${NC}"
            echo -e "${YELLOW}Please install Ollama manually from: https://ollama.ai${NC}"
        fi
    fi
fi

# Check for sample documents
echo -e "${YELLOW}Checking sample documents...${NC}"
if [ -d "documents" ]; then
    DOC_COUNT=$(find documents -type f | wc -l)
    if [ "$DOC_COUNT" -gt 0 ]; then
        echo -e "${GREEN}✓ Found $DOC_COUNT sample documents in 'documents' folder${NC}"
    else
        echo -e "${YELLOW}⚠ No documents found in 'documents' folder${NC}"
        echo -e "${YELLOW}Add PDF, TXT, DOCX, or MD files to the 'documents' folder for testing${NC}"
    fi
else
    echo -e "${YELLOW}⚠ Documents folder not found${NC}"
fi

echo ""
echo -e "${CYAN}=====================================${NC}"
echo -e "${CYAN}Setup Complete!${NC}"
echo -e "${CYAN}=====================================${NC}"
echo ""

echo -e "${NC}Next Steps:${NC}"
echo -e "${NC}1. Add documents to the 'documents' folder${NC}"
echo -e "${NC}2. Index documents:${NC}"
echo -e "   ${YELLOW}cd VectorIndexer${NC}"
echo -e "   ${YELLOW}dotnet run${NC}"
echo -e "${NC}3. Start the chat interface:${NC}"
echo -e "   ${YELLOW}cd RagChat${NC}"
echo -e "   ${YELLOW}dotnet run${NC}"
echo ""

if [ "$SKIP_OLLAMA" = true ]; then
    echo -e "${YELLOW}Note: Ollama setup was skipped. Make sure to install and configure Ollama manually.${NC}"
fi

echo -e "${CYAN}For detailed instructions, see DEPLOYMENT-GUIDE.md${NC}" 