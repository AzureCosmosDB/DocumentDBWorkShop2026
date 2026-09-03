using Azure.Core;
using Azure.Identity;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Authentication.Oidc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string AzureOpenAiScope = "https://ai.azure.com/.default";
    private const string DocumentDbScope = "https://ossrdbms-aad.database.windows.net/.default";

    public static async Task<int> Main()
    {
        try
        {
            var clusterName = GetRequiredEnvironmentVariable("DOCUMENTDB_CLUSTER_NAME");
            var azureOpenAiEndpoint = GetRequiredEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var embeddingDeployment = GetRequiredEnvironmentVariable(
                "AZURE_OPENAI_EMBEDDING_DEPLOYMENT");
            var credential = new AzureCliCredential();

            var mongoUrl = MongoUrl.Create(
                $"mongodb+srv://{clusterName}.global.mongocluster.cosmos.azure.com/");
            var settings = MongoClientSettings.FromUrl(mongoUrl);
            settings.UseTls = true;
            settings.RetryWrites = false;
            settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(2);
            settings.Credential = MongoCredential.CreateOidcCredential(
                new AzureCliTokenHandler(credential));
            settings.Freeze();

            using var httpClient = new HttpClient();
            var client = new MongoClient(settings);
            var database = client.GetDatabase("docdbworkshop");
            var chunks = database.GetCollection<BsonDocument>("rag_chunks");
            await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            Console.WriteLine("Connected to Azure DocumentDB with Microsoft Entra ID.");

            SourceChunk[] sourceChunks =
            [
                new("rag-001", "search-module", "Vector search", "Azure DocumentDB vector search uses the $search stage with the cosmosSearch operator to retrieve documents by embedding similarity.", "module-4-search", ["vector", "search"]),
                new("rag-002", "search-module", "Full-text search", "Azure DocumentDB full-text search uses createSearchIndexes and the $search text operator to return BM25-ranked keyword matches.", "module-4-search", ["full-text", "bm25"]),
                new("rag-003", "search-module", "Hybrid search", "Hybrid search runs BM25 keyword retrieval and vector retrieval, then combines ranked lists with Reciprocal Rank Fusion.", "module-4-search", ["hybrid", "rrf"]),
                new("rag-004", "rag-module", "Grounded generation", "A RAG pipeline retrieves relevant chunks from Azure DocumentDB and includes them in the model prompt so the answer is grounded in current application data.", "module-5-rag", ["rag", "generation"])
            ];

            var documents = new List<BsonDocument>();
            foreach (var sourceChunk in sourceChunks)
            {
                var embedding = await CreateEmbeddingAsync(
                    credential,
                    httpClient,
                    azureOpenAiEndpoint,
                    embeddingDeployment,
                    sourceChunk.Chunk);
                documents.Add(new BsonDocument
                {
                    { "_id", sourceChunk.Id },
                    { "sourceId", sourceChunk.SourceId },
                    { "title", sourceChunk.Title },
                    { "chunk", sourceChunk.Chunk },
                    { "url", sourceChunk.Url },
                    { "tags", new BsonArray(sourceChunk.Tags) },
                    { "embedding", embedding }
                });
            }

            try
            {
                await database.DropCollectionAsync("rag_chunks");
            }
            catch (MongoCommandException exception) when (exception.CodeName == "NamespaceNotFound")
            {
            }

            chunks = database.GetCollection<BsonDocument>("rag_chunks");
            await chunks.InsertManyAsync(documents);
            var embeddingDimensions = documents[0]["embedding"].AsBsonArray.Count;
            Console.WriteLine(
                $"Loaded {documents.Count} chunks with {embeddingDimensions}-dimension embeddings.");

            await database.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "createIndexes", "rag_chunks" },
                { "indexes", new BsonArray
                    {
                        new BsonDocument
                        {
                            { "name", "idx_chunk_embedding_diskann" },
                            { "key", new BsonDocument("embedding", "cosmosSearch") },
                            { "cosmosSearchOptions", new BsonDocument
                                {
                                    { "kind", "vector-diskann" },
                                    { "dimensions", embeddingDimensions },
                                    { "similarity", "COS" },
                                    { "maxDegree", 32 },
                                    { "lBuild", 64 }
                                }
                            }
                        }
                    }
                }
            });

            var fullTextSearchSupported = true;
            try
            {
                await database.RunCommandAsync<BsonDocument>(new BsonDocument
                {
                    { "createSearchIndexes", "rag_chunks" },
                    { "indexes", new BsonArray
                        {
                            new BsonDocument
                            {
                                { "name", "idx_chunk_fts" },
                                { "definition", new BsonDocument("mappings", new BsonDocument
                                    {
                                        { "dynamic", false },
                                        { "fields", new BsonDocument("chunk", new BsonDocument("type", "string")) }
                                    })
                                }
                            }
                        }
                    }
                });
                Console.WriteLine("Created vector and full-text retrieval indexes.");
            }
            catch (MongoCommandException exception) when (exception.Code == 115)
            {
                fullTextSearchSupported = false;
                Console.Error.WriteLine(
                    "Full-text search is not enabled for this cluster; continuing with vector-only retrieval.");
            }

            const string question = "How does DocumentDB retrieve context for RAG?";
            var questionVector = await CreateEmbeddingAsync(
                credential,
                httpClient,
                azureOpenAiEndpoint,
                embeddingDeployment,
                question);
            var vectorContext = await chunks.Aggregate<BsonDocument>(
            [
                new BsonDocument("$search", new BsonDocument("cosmosSearch", new BsonDocument
                {
                    { "path", "embedding" },
                    { "vector", questionVector },
                    { "k", 3 },
                    { "lSearch", 40 }
                })),
                new BsonDocument("$project", new BsonDocument
                {
                    { "_id", 1 },
                    { "title", 1 },
                    { "chunk", 1 },
                    { "url", 1 },
                    { "score", new BsonDocument("$meta", "searchScore") }
                })
            ]).ToListAsync();

            IReadOnlyList<BsonDocument> retrievedContext = vectorContext;
            if (fullTextSearchSupported)
            {
                var keywordContext = await chunks.Aggregate<BsonDocument>(
                [
                    new BsonDocument("$search", new BsonDocument
                    {
                        { "index", "idx_chunk_fts" },
                        { "text", new BsonDocument
                            {
                                { "query", question },
                                { "path", "chunk" }
                            }
                        }
                    }),
                    new BsonDocument("$limit", 3),
                    new BsonDocument("$project", new BsonDocument
                    {
                        { "_id", 1 },
                        { "title", 1 },
                        { "chunk", 1 },
                        { "url", 1 },
                        { "score", new BsonDocument("$meta", "searchScore") }
                    })
                ]).ToListAsync();
                retrievedContext = FuseWithRrf([keywordContext, vectorContext]);
            }

            var contextBlock = string.Join(
                "\n\n",
                retrievedContext.Select((document, index) =>
                    $"[{index + 1}] {document["title"]}\n{document["chunk"]}\nSource: {document["url"]}"));
            var groundedPrompt = $"""
                You are a helpful assistant for an Azure DocumentDB workshop.
                Answer the user's question using only the context below.
                If the context does not contain the answer, say you do not know based on the provided context.

                <context>
                {contextBlock}
                </context>

                Question: {question}
                """;

            Console.WriteLine("\nGrounded prompt\n");
            Console.WriteLine(groundedPrompt);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(
                $"Missing {name}. Run ../../../1-DocumentDB-Introduction-and-Cluster-Setup/Set-LabEnvironment.ps1 first.")
            : value;
    }

    private static async Task<BsonArray> CreateEmbeddingAsync(
        TokenCredential credential,
        HttpClient httpClient,
        string endpoint,
        string deployment,
        string text)
    {
        var token = await credential.GetTokenAsync(
            new TokenRequestContext([AzureOpenAiScope]),
            CancellationToken.None);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{endpoint.TrimEnd('/')}/openai/v1/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { model = deployment, input = text }),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Azure OpenAI returned {(int)response.StatusCode}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        return new BsonArray(
            json.RootElement.GetProperty("data")[0].GetProperty("embedding")
                .EnumerateArray()
                .Select(value => value.GetDouble()));
    }

    private static IReadOnlyList<BsonDocument> FuseWithRrf(
        IReadOnlyList<IReadOnlyList<BsonDocument>> resultLists,
        int rankConstant = 60,
        int limit = 3)
    {
        Dictionary<string, double> scores = new();
        Dictionary<string, BsonDocument> documents = new();

        foreach (var results in resultLists)
        {
            for (var rank = 0; rank < results.Count; rank++)
            {
                var id = results[rank]["_id"].ToString();
                documents[id] = results[rank];
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (rankConstant + rank + 1);
            }
        }

        return scores
            .OrderByDescending(item => item.Value)
            .Take(limit)
            .Select(item => documents[item.Key])
            .ToList();
    }

    private sealed record SourceChunk(
        string Id,
        string SourceId,
        string Title,
        string Chunk,
        string Url,
        IReadOnlyList<string> Tags);

    private sealed class AzureCliTokenHandler(TokenCredential credential) : IOidcCallback
    {
        private static readonly string[] Scopes = [DocumentDbScope];

        public OidcAccessToken GetOidcAccessToken(
            OidcCallbackParameters parameters,
            CancellationToken cancellationToken)
        {
            _ = parameters;
            var token = credential.GetToken(new TokenRequestContext(Scopes), cancellationToken);
            return new OidcAccessToken(
                token.Token,
                token.ExpiresOn - DateTimeOffset.UtcNow);
        }

        public async Task<OidcAccessToken> GetOidcAccessTokenAsync(
            OidcCallbackParameters parameters,
            CancellationToken cancellationToken)
        {
            _ = parameters;
            var token = await credential.GetTokenAsync(
                new TokenRequestContext(Scopes),
                cancellationToken);
            return new OidcAccessToken(
                token.Token,
                token.ExpiresOn - DateTimeOffset.UtcNow);
        }
    }
}
