using Microsoft.Data.Sqlite;

var dbPath = "../VectorIndexer/data/vectors.db";

if (!File.Exists(dbPath))
{
    Console.WriteLine($"Database file not found: {dbPath}");
    return;
}

using var connection = new SqliteConnection($"Data Source={dbPath}");
connection.Open();

// Check total chunks
var countCommand = connection.CreateCommand();
countCommand.CommandText = "SELECT COUNT(*) FROM document_chunks";
var totalChunks = Convert.ToInt32(countCommand.ExecuteScalar());
Console.WriteLine($"Total chunks in database: {totalChunks}");

// Check Software-Architecture-Patterns.pdf chunks specifically
var softwareArchCommand = connection.CreateCommand();
softwareArchCommand.CommandText = @"
    SELECT page_number, SUBSTR(text, 1, 300) 
    FROM document_chunks 
    WHERE document_name = 'Software-Architecture-Patterns.pdf' 
    AND (text LIKE '%layered%' OR text LIKE '%layer%' OR text LIKE '%architecture%')
    LIMIT 10";

Console.WriteLine("Software-Architecture-Patterns.pdf chunks containing architecture/layer terms:");
using var reader = softwareArchCommand.ExecuteReader();
while (reader.Read())
{
    Console.WriteLine($"Page {reader.GetInt32(0)}: {reader.GetString(1)}...\n");
}
reader.Close();

// Also check first few chunks to see what's actually in the document
var firstChunksCommand = connection.CreateCommand();
firstChunksCommand.CommandText = @"
    SELECT page_number, SUBSTR(text, 1, 200) 
    FROM document_chunks 
    WHERE document_name = 'Software-Architecture-Patterns.pdf' 
    ORDER BY page_number, chunk_index
    LIMIT 5";

Console.WriteLine("\nFirst 5 chunks from Software-Architecture-Patterns.pdf:");
using var firstReader = firstChunksCommand.ExecuteReader();
while (firstReader.Read())
{
    Console.WriteLine($"Page {firstReader.GetInt32(0)}: {firstReader.GetString(1)}...\n");
}

// Search for "layered" in text content across all documents
var layeredCommand = connection.CreateCommand();
layeredCommand.CommandText = "SELECT document_name, page_number, SUBSTR(text, 1, 200) FROM document_chunks WHERE text LIKE '%layered%' OR text LIKE '%layer%' LIMIT 5";
using var layeredReader = layeredCommand.ExecuteReader();
Console.WriteLine("\nChunks containing 'layered' or 'layer' (all documents):");
while (layeredReader.Read())
{
    Console.WriteLine($"  Doc: {layeredReader.GetString(0)}, Page: {layeredReader.GetInt32(1)}");
    Console.WriteLine($"  Text: {layeredReader.GetString(2)}...\n");
}
