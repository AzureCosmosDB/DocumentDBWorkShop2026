using Azure.Core;
using Azure.Identity;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using MongoDB.Driver.Authentication.Oidc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private const string DocumentDbScope = "https://ossrdbms-aad.database.windows.net/.default";
    private const string AzureOpenAiScope = "https://ai.azure.com/.default";

    public static async Task<int> Main()
    {
        try
        {
            var clusterName = GetRequiredEnvironmentVariable("DOCUMENTDB_CLUSTER_NAME");
            var azureOpenAiEndpoint = GetRequiredEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var embeddingDeployment = GetRequiredEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT");
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
            var collection = database.GetCollection<BsonDocument>("workshop_content");
            await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
            Console.WriteLine("Connected to Azure DocumentDB with Microsoft Entra ID.");

            var sourceDocuments = new[]
            {
                new SourceDocument("doc-search-001", "DiskANN vector indexing", "vector", "Azure DocumentDB supports DiskANN vector indexes for high recall semantic similarity search over embeddings stored with documents.", "SEARCH-VEC-001"),
                new SourceDocument("doc-search-002", "BM25 keyword search", "full-text", "Azure DocumentDB full-text search ranks keyword matches with BM25 and exposes scores through searchScore metadata.", "SEARCH-FTS-001"),
                new SourceDocument("doc-search-003", "Hybrid search with RRF", "hybrid", "Hybrid search combines BM25 keyword results with vector results and fuses the ranked lists using Reciprocal Rank Fusion.", "SEARCH-HYB-001"),
                new SourceDocument("doc-search-004", "RAG grounding", "rag", "Retrieval augmented generation retrieves relevant chunks from Azure DocumentDB and grounds the model answer in that context.", "RAG-PIPE-001"),
                new SourceDocument("doc-search-005", "Operational filtering", "filters", "Search applications often filter by status, tenant, region, stock, or category after the search stage narrows candidate documents.", "SEARCH-FLT-001")
            };

            var documents = new List<BsonDocument>();
            foreach (var item in sourceDocuments)
            {
                var embedding = await CreateEmbeddingAsync(
                    credential,
                    httpClient,
                    azureOpenAiEndpoint,
                    embeddingDeployment,
                    item.Body);
                documents.Add(new BsonDocument
                {
                    { "_id", item.Id },
                    { "title", item.Title },
                    { "category", item.Category },
                    { "body", item.Body },
                    { "sku", item.Sku },
                    { "embedding", embedding }
                });
            }

            try
            {
                await database.DropCollectionAsync("workshop_content");
            }
            catch (MongoCommandException exception) when (exception.CodeName == "NamespaceNotFound")
            {
            }

            collection = database.GetCollection<BsonDocument>("workshop_content");
            await collection.InsertManyAsync(documents);
            var embeddingDimensions = documents[0]["embedding"].AsBsonArray.Count;
            Console.WriteLine(
                $"Loaded {documents.Count} documents with {embeddingDimensions}-dimension embeddings.");

            await database.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "createIndexes", "workshop_content" },
                { "indexes", new BsonArray
                    {
                        new BsonDocument
                        {
                            { "name", "idx_embedding_diskann" },
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
                    { "createSearchIndexes", "workshop_content" },
                    { "indexes", new BsonArray
                        {
                            new BsonDocument
                            {
                                { "name", "idx_body_fts" },
                                { "definition", new BsonDocument("mappings", new BsonDocument
                                    {
                                        { "dynamic", false },
                                        { "fields", new BsonDocument("body", new BsonDocument("type", "string")) }
                                    })
                                }
                            }
                        }
                    }
                });
                Console.WriteLine("Created vector and full-text indexes.");
            }
            catch (MongoCommandException exception) when (exception.Code == 115)
            {
                fullTextSearchSupported = false;
                Console.Error.WriteLine(
                    "Full-text search is not enabled for this cluster; continuing with vector-only search.");
            }

            const string searchText = "semantic retrieval for RAG";
            var queryVector = await CreateEmbeddingAsync(
                credential,
                httpClient,
                azureOpenAiEndpoint,
                embeddingDeployment,
                searchText);

            var vectorResults = await collection.Aggregate<BsonDocument>(new[]
            {
                new BsonDocument("$search", new BsonDocument("cosmosSearch", new BsonDocument
                {
                    { "path", "embedding" },
                    { "vector", queryVector },
                    { "k", 3 },
                    { "lSearch", 40 }
                })),
                new BsonDocument("$project", new BsonDocument
                {
                    { "_id", 1 }, { "title", 1 }, { "category", 1 },
                    { "score", new BsonDocument("$meta", "searchScore") }
                })
            }).ToListAsync();
            PrintResults("Vector search", vectorResults);

            if (fullTextSearchSupported)
            {
                var bm25Results = await collection.Aggregate<BsonDocument>(new[]
                {
                    new BsonDocument("$search", new BsonDocument
                    {
                        { "index", "idx_body_fts" },
                        { "text", new BsonDocument { { "query", "BM25 ranking" }, { "path", "body" } } }
                    }),
                    new BsonDocument("$limit", 5),
                    new BsonDocument("$project", new BsonDocument
                    {
                        { "_id", 1 }, { "title", 1 },
                        { "score", new BsonDocument("$meta", "searchScore") }
                    })
                }).ToListAsync();
                PrintResults("BM25 search", bm25Results);

                var fuzzyResults = await collection.Aggregate<BsonDocument>(new[]
                {
                    new BsonDocument("$search", new BsonDocument
                    {
                        { "index", "idx_body_fts" },
                        { "text", new BsonDocument
                            {
                                { "query", "retrival augmentd genration" },
                                { "path", "body" },
                                { "fuzzy", new BsonDocument("maxEdits", 1) }
                            }
                        }
                    }),
                    new BsonDocument("$limit", 5),
                    new BsonDocument("$project", new BsonDocument
                    {
                        { "_id", 1 }, { "title", 1 },
                        { "score", new BsonDocument("$meta", "searchScore") }
                    })
                }).ToListAsync();
                PrintResults("Fuzzy search", fuzzyResults);

                var phraseResults = await collection.Aggregate<BsonDocument>(new[]
                {
                    new BsonDocument("$search", new BsonDocument
                    {
                        { "index", "idx_body_fts" },
                        { "phrase", new BsonDocument
                        {
                            { "query", "Reciprocal Rank Fusion" },
                            { "path", "body" },
                            { "slop", 0 }
                        }
                        }
                    }),
                    new BsonDocument("$limit", 5),
                    new BsonDocument("$project", new BsonDocument
                    {
                        { "_id", 1 }, { "title", 1 },
                        { "score", new BsonDocument("$meta", "searchScore") }
                    })
                }).ToListAsync();
                PrintResults("Phrase search", phraseResults);

                var hybridKeywordResults = await collection.Aggregate<BsonDocument>(new[]
                {
                    new BsonDocument("$search", new BsonDocument
                    {
                        { "index", "idx_body_fts" },
                        { "text", new BsonDocument { { "query", searchText }, { "path", "body" } } }
                    }),
                    new BsonDocument("$limit", 5),
                    new BsonDocument("$project", new BsonDocument { { "_id", 1 }, { "title", 1 } })
                }).ToListAsync();
                var hybridVectorResults = await collection.Aggregate<BsonDocument>(new[]
                {
                    new BsonDocument("$search", new BsonDocument("cosmosSearch", new BsonDocument
                        {
                        { "path", "embedding" },
                        { "vector", queryVector },
                        { "k", 5 },
                        { "lSearch", 40 }
                    })),
                    new BsonDocument("$project", new BsonDocument { { "_id", 1 }, { "title", 1 } })
                }).ToListAsync();

                Console.WriteLine("\nHybrid search with RRF");
                foreach (var result in FuseWithRrf([hybridKeywordResults, hybridVectorResults]))
                {
                    Console.WriteLine($"{result.Title} | {result.Score:F6}");
                }
            }

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

    private static IReadOnlyList<RrfResult> FuseWithRrf(
        IReadOnlyList<IReadOnlyList<BsonDocument>> resultLists,
        int rankConstant = 60,
        int limit = 5)
    {
        Dictionary<string, double> scores = new();
        Dictionary<string, string> titles = new();

        foreach (var results in resultLists)
        {
            for (var rank = 0; rank < results.Count; rank++)
            {
                var id = results[rank]["_id"].ToString();
                titles[id] = results[rank]["title"].AsString;
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (rankConstant + rank + 1);
            }
        }

        return scores
            .OrderByDescending(item => item.Value)
            .Take(limit)
            .Select(item => new RrfResult(titles[item.Key], item.Value))
            .ToList();
    }

    private static void PrintResults(string heading, IReadOnlyCollection<BsonDocument> results)
    {
        Console.WriteLine($"\n{heading}");
        var settings = new JsonWriterSettings { Indent = true };
        foreach (var result in results)
        {
            Console.WriteLine(result.ToJson(settings));
        }
    }

    private sealed record SourceDocument(
        string Id,
        string Title,
        string Category,
        string Body,
        string Sku);

    private sealed record RrfResult(string Title, double Score);

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
