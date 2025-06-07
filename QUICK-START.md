# RAG System Quick Start Guide

Get your local RAG system up and running in minutes!

## Prerequisites

- .NET 9 SDK installed
- Windows 10/11, macOS, or Linux

## Automated Setup

### Windows (PowerShell)
```powershell
.\setup.ps1
```

### macOS/Linux (Bash)
```bash
chmod +x setup.sh
./setup.sh
```

## Manual Setup (5 Steps)

### 1. Install Ollama
Download and install from [ollama.ai](https://ollama.ai), then:
```bash
ollama pull llama2
```

### 2. Build the Project
```bash
dotnet restore
dotnet build
```

### 3. Setup Configuration
```bash
# Windows
copy "Ragtut.Core\appsettings.template.json" "VectorIndexer\appsettings.json"
copy "Ragtut.Core\appsettings.template.json" "RagChat\appsettings.json"

# macOS/Linux
cp Ragtut.Core/appsettings.template.json VectorIndexer/appsettings.json
cp Ragtut.Core/appsettings.template.json RagChat/appsettings.json
```

### 4. Index Documents
```bash
cd VectorIndexer
dotnet run
```

### 5. Start Chatting
```bash
cd ../RagChat
dotnet run
```

## Test the System

Try these sample queries:
- "What is RAG?"
- "Explain software architecture patterns"
- "How does Docker work?"

## Troubleshooting

**Build fails?** Run: `dotnet clean && dotnet restore && dotnet build`

**Ollama not working?** Check: `http://localhost:11434`

**No responses?** Ensure documents are in the `documents/` folder and indexing completed successfully.

---

**Need more help?** See [DEPLOYMENT-GUIDE.md](DEPLOYMENT-GUIDE.md) for detailed instructions. 