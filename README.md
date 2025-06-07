# Ragtut - Local RAG System

A local RAG (Retrieval-Augmented Generation) system built with .NET 9, featuring document indexing and chat capabilities.

## Project Structure

- **Ragtut.Core**: Shared library containing core functionality
  - Models: Data models and DTOs
  - Services: Core business logic
  - Interfaces: Service contracts
  - Extensions: Helper methods and extensions

- **VectorIndexer**: Console application for indexing documents
  - Processes PDF, TXT, and DOCX files
  - Chunks text with overlap
  - Stores vectors in SQLite database

- **RagChat**: Console application for querying documents
  - Uses local LLM (Ollama)
  - Performs vector similarity search
  - Provides citations for answers

## Prerequisites

- .NET 9 SDK
- SQLite with vector extensions
- Ollama or similar local LLM

## Setup

1. Clone the repository
2. Place your documents in the `documents` folder
3. Run the VectorIndexer to process documents:
   ```
   dotnet run --project VectorIndexer
   ```
4. Start the chat application:
   ```
   dotnet run --project RagChat
   ```

## Features

- Local vector database using SQLite
- Support for multiple document formats (PDF, TXT, DOCX)
- Text chunking with overlap for better retrieval
- Local LLM integration
- Citation tracking for answers
- Modern console UI with Spectre.Console

## Dependencies

### Core Library
- Microsoft.Data.Sqlite
- itext7 (PDF processing)
- DocumentFormat.OpenXml (DOCX processing)
- Microsoft.Extensions.Logging.Abstractions
- Microsoft.Extensions.DependencyInjection.Abstractions

### VectorIndexer
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging.Console
- Microsoft.Extensions.Configuration.Json

### RagChat
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging.Console
- Microsoft.Extensions.Configuration.Json
- Spectre.Console (Console UI) 