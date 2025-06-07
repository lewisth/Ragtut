# RAG System Deployment Guide

This guide provides comprehensive instructions for deploying and using the local RAG (Retrieval-Augmented Generation) system built with .NET 9.

## Table of Contents
1. [System Overview](#system-overview)
2. [Prerequisites](#prerequisites)
3. [Step-by-Step Setup](#step-by-step-setup)
4. [Local LLM Installation (Ollama)](#local-llm-installation-ollama)
5. [Vector Database Setup](#vector-database-setup)
6. [Configuration](#configuration)
7. [Sample Documents and Testing](#sample-documents-and-testing)
8. [Troubleshooting](#troubleshooting)
9. [Performance Tuning](#performance-tuning)
10. [Maintenance](#maintenance)

## System Overview

The RAG system consists of three main components:

- **Ragtut.Core**: Shared library with models, services, and interfaces
- **VectorIndexer**: Console application for processing and indexing documents
- **RagChat**: Interactive console application for querying the knowledge base

### Architecture
```
Documents → VectorIndexer → SQLite Vector DB → RagChat → Ollama LLM
```

## Prerequisites

### System Requirements
- **Operating System**: Windows 10/11, macOS, or Linux
- **RAM**: Minimum 8GB (16GB recommended for better performance)
- **Storage**: At least 10GB free space for models and data
- **Network**: Internet connection for initial setup and model downloads

### Software Dependencies
- **.NET 9 SDK** - Latest version
- **SQLite** - Version 3.38+ with vector extensions
- **Ollama** - For local LLM hosting
- **Git** - For cloning the repository

## Step-by-Step Setup

### 1. Clone the Repository

```bash
git clone <repository-url>
cd Ragtut
```

### 2. Verify .NET Installation

```bash
dotnet --version
```
Ensure you have .NET 9.0 or later installed.

### 3. Restore Dependencies

```bash
dotnet restore
```

### 4. Build the Solution

```bash
dotnet build
```

### 5. Create Required Directories

```bash
mkdir -p data
mkdir -p logs
mkdir -p temp
mkdir -p models
```

### 6. Configure Settings

Copy the template configuration:

```bash
cp Ragtut.Core/appsettings.template.json VectorIndexer/appsettings.json
cp Ragtut.Core/appsettings.template.json RagChat/appsettings.json
```

## Local LLM Installation (Ollama)

### Install Ollama

#### Windows
1. Download Ollama from [ollama.ai](https://ollama.ai)
2. Run the installer
3. Open Command Prompt or PowerShell

#### macOS
```bash
curl -fsSL https://ollama.ai/install.sh | sh
```

#### Linux
```bash
curl -fsSL https://ollama.ai/install.sh | sh
```

### Download and Setup Models

#### Recommended Models

**For General Use:**
```bash
ollama pull llama2
```

**For Better Performance (if you have sufficient RAM):**
```bash
ollama pull llama2:13b
```

**For Faster Responses:**
```bash
ollama pull phi
```

**For Code-Related Questions:**
```bash
ollama pull codellama
```

### Verify Ollama Installation

```bash
ollama list
```

### Start Ollama Service

#### Windows/macOS
Ollama typically starts automatically. Verify by accessing:
```
http://localhost:11434
```

#### Linux (if not auto-started)
```bash
ollama serve
```

### Test Ollama

```bash
ollama run llama2
```
Type a test message and press Ctrl+D to exit.

## Vector Database Setup

The system uses SQLite with vector extensions for storing document embeddings.

### Automatic Setup
The system will automatically create the SQLite database on first run. No manual setup required.

### Manual Database Initialization (Optional)

If you want to manually create the database:

```bash
sqlite3 data/vectors.db
```

In SQLite prompt:
```sql
-- Enable vector extension (if available)
.load vector

-- Create tables (this will be done automatically by the application)
-- Just verify the database is accessible
.tables
.quit
```

### Database Location
- Default path: `data/vectors.db`
- Configurable in `appsettings.json` under `VectorStore.DatabasePath`

## Configuration

### Basic Configuration

Edit `appsettings.json` in both `VectorIndexer` and `RagChat` projects:

```json
{
  "DocumentProcessing": {
    "ChunkSize": 800,
    "ChunkOverlap": 100,
    "SupportedExtensions": [".pdf", ".txt", ".docx", ".md"]
  },
  "EmbeddingModel": {
    "ModelPath": "models/all-MiniLM-L6-v2.onnx",
    "Dimension": 384
  },
  "VectorStore": {
    "DatabasePath": "data/vectors.db"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "llama2",
    "Temperature": 0.7,
    "MaxTokens": 1024
  },
  "RAG": {
    "MaxChunks": 5,
    "SimilarityThreshold": 0.7
  }
}
```

### Advanced Configuration Options

For detailed configuration options, see `Ragtut.Core/README-Configuration.md`.

Key settings to consider:

- **ChunkSize**: Adjust based on your documents (400-1000 characters)
- **ChunkOverlap**: Overlap between chunks (50-200 characters)
- **MaxChunks**: Number of chunks to retrieve for each query (3-10)
- **SimilarityThreshold**: Minimum similarity score (0.5-0.8)
- **Temperature**: LLM creativity (0.1 for factual, 0.9 for creative)

## Sample Documents and Testing

### Provided Sample Documents

The system comes with sample documents in the `documents/` folder:

- Technical books (Software Architecture, Docker, etc.)
- Programming guides (Rails, .NET, etc.)
- Cloud computing resources (AWS, Terraform, etc.)
- Data science materials (Machine Learning, Delta Lake, etc.)

### Adding Your Own Documents

1. Place documents in the `documents/` folder
2. Supported formats: PDF, TXT, DOCX, MD
3. Recommended file size: Under 100MB per document

### Testing the System

#### 1. Index Documents

```bash
cd VectorIndexer
dotnet run
```

The indexer will:
- Process all documents in the `documents/` folder
- Split them into chunks
- Generate embeddings
- Store vectors in the database

#### 2. Start Chat Interface

```bash
cd RagChat
dotnet run
```

#### 3. Test Queries

Try these sample queries:

**General Questions:**
- "What is software architecture?"
- "Explain microservices patterns"
- "How to deploy applications with Docker?"

**Specific Technical Questions:**
- "What are the SOLID principles?"
- "How to implement event-driven architecture?"
- "Best practices for database design"

**Code-Related Questions:**
- "Show me examples of design patterns"
- "How to refactor legacy code?"
- "What are Rails conventions?"

### Expected Output

The system will:
1. Find relevant document chunks
2. Generate a response using the local LLM
3. Provide citations showing which documents were used
4. Display similarity scores for transparency

## Troubleshooting

### Common Issues and Solutions

#### 1. Build Errors

**Issue**: Missing dependencies or SDK issues
```
error NU1605: Detected package downgrade
```

**Solution**:
```bash
dotnet clean
dotnet restore --force
dotnet build
```

#### 2. Ollama Connection Issues

**Issue**: Cannot connect to Ollama
```
HttpRequestException: Connection refused
```

**Solutions**:
- Verify Ollama is running: `ollama list`
- Check the service: `http://localhost:11434`
- Restart Ollama service
- Check firewall settings

#### 3. Database Issues

**Issue**: SQLite database errors
```
SqliteException: database is locked
```

**Solutions**:
- Close any running instances of the applications
- Delete `data/vectors.db` and reindex
- Check file permissions
- Ensure WAL mode is enabled

#### 4. Memory Issues

**Issue**: Out of memory during indexing
```
OutOfMemoryException
```

**Solutions**:
- Reduce `ChunkSize` in configuration
- Process fewer documents at once
- Increase system RAM
- Adjust `MaxConcurrentProcessing` setting

#### 5. Document Processing Errors

**Issue**: PDF processing failures
```
iTextSharp.text.exceptions.InvalidPdfException
```

**Solutions**:
- Verify PDF is not corrupted
- Try with a different PDF
- Check file permissions
- Update to latest iText7 version

#### 6. Slow Performance

**Issue**: Queries take too long
**Solutions**:
- Reduce `MaxChunks` in configuration
- Increase `SimilarityThreshold` to get fewer but more relevant results
- Use a smaller, faster LLM model (e.g., `phi` instead of `llama2`)
- Optimize database with VACUUM

### Debug Mode

Enable detailed logging by modifying `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Ragtut": "Trace"
    }
  }
}
```

### Log Files

Check log files in the `logs/` directory for detailed error information.

## Performance Tuning

### Hardware Optimization

#### CPU
- **Minimum**: 4 cores
- **Recommended**: 8+ cores for better parallel processing
- **Optimal**: Modern CPU with AVX2 support for embedding calculations

#### Memory
- **Minimum**: 8GB RAM
- **Recommended**: 16GB RAM
- **Optimal**: 32GB+ for large document sets

#### Storage
- **SSD**: Strongly recommended for database operations
- **NVMe**: Best performance for large-scale deployments

### Software Optimization

#### 1. Chunking Strategy

**Small Documents (< 10 pages)**:
```json
{
  "ChunkSize": 400,
  "ChunkOverlap": 50
}
```

**Medium Documents (10-100 pages)**:
```json
{
  "ChunkSize": 800,
  "ChunkOverlap": 100
}
```

**Large Documents (> 100 pages)**:
```json
{
  "ChunkSize": 1200,
  "ChunkOverlap": 150
}
```

#### 2. Retrieval Optimization

**High Precision** (fewer, more relevant results):
```json
{
  "MaxChunks": 3,
  "SimilarityThreshold": 0.8
}
```

**High Recall** (more comprehensive coverage):
```json
{
  "MaxChunks": 8,
  "SimilarityThreshold": 0.6
}
```

#### 3. LLM Optimization

**Fast Responses**:
```json
{
  "Model": "phi",
  "MaxTokens": 512,
  "Temperature": 0.3
}
```

**Detailed Responses**:
```json
{
  "Model": "llama2:13b",
  "MaxTokens": 2048,
  "Temperature": 0.7
}
```

#### 4. Database Optimization

Enable WAL mode and optimize SQLite:
```json
{
  "VectorStore": {
    "EnableWalMode": true,
    "QueryBatchSize": 100,
    "IndexBatchSize": 50
  }
}
```

Periodic database maintenance:
```bash
sqlite3 data/vectors.db "VACUUM;"
sqlite3 data/vectors.db "REINDEX;"
```

#### 5. Memory Management

```json
{
  "Memory": {
    "MaxDocumentCacheSizeMB": 500,
    "MaxEmbeddingCacheSizeMB": 200,
    "EnableGarbageCollectionTuning": true
  }
}
```

### Performance Monitoring

Enable performance tracking:
```json
{
  "Performance": {
    "EnableMetrics": true,
    "LogSlowOperations": true,
    "SlowOperationThresholdMs": 5000
  }
}
```

### Benchmarking

Use these queries to benchmark your system:

1. **Simple Query**: "What is X?"
2. **Complex Query**: "Compare A and B, explain the differences"
3. **Multi-document Query**: "Find information about X across all documents"

Measure:
- **Indexing Time**: Time to process all documents
- **Query Response Time**: End-to-end query processing
- **Memory Usage**: Peak memory during operations
- **Database Size**: Final database file size

## Maintenance

### Regular Tasks

#### Weekly
- Check log files for errors
- Monitor disk space usage
- Verify Ollama service is running

#### Monthly
- Update Ollama models
- Clean up temporary files
- Review and optimize configuration
- Backup vector database

#### As Needed
- Add new documents and reindex
- Update .NET SDK and dependencies
- Tune performance based on usage patterns

### Backup Strategy

**Database Backup**:
```bash
cp data/vectors.db data/vectors.db.backup
```

**Configuration Backup**:
```bash
cp VectorIndexer/appsettings.json config/appsettings.backup.json
cp RagChat/appsettings.json config/appsettings-chat.backup.json
```

### Updates

**Update Dependencies**:
```bash
dotnet list package --outdated
dotnet add package <PackageName>
```

**Update Ollama**:
```bash
ollama pull llama2  # Re-download latest model
```

### Scaling Considerations

For production deployment:
- Consider PostgreSQL instead of SQLite for better concurrency
- Implement proper authentication and authorization
- Add API endpoints for web integration
- Set up monitoring and alerting
- Implement horizontal scaling with load balancers

---

## Support

For additional help:
1. Check the logs in `logs/` directory
2. Review the configuration documentation in `Ragtut.Core/README-Configuration.md`
3. Verify system requirements and dependencies
4. Test with provided sample documents first
5. Check Ollama service status and model availability 