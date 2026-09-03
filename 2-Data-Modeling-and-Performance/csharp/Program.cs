using Azure.Core;
using Azure.Identity;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Authentication.Oidc;

internal static class Program
{
    private const string DocumentDbScope = "https://ossrdbms-aad.database.windows.net/.default";

    public static async Task<int> Main()
    {
        try
        {
            var clusterName = GetRequiredEnvironmentVariable("DOCUMENTDB_CLUSTER_NAME");
            var credential = new AzureCliCredential();
            var settings = MongoClientSettings.FromUrl(MongoUrl.Create(
                $"mongodb+srv://{clusterName}.global.mongocluster.cosmos.azure.com/"));
            settings.UseTls = true;
            settings.RetryWrites = false;
            settings.Credential = MongoCredential.CreateOidcCredential(new AzureCliTokenHandler(credential));
            settings.Freeze();

            var database = new MongoClient(settings).GetDatabase("docdbworkshop");
            var customers = database.GetCollection<BsonDocument>("customers");
            var orders = database.GetCollection<BsonDocument>("orders");
            var demo = database.GetCollection<BsonDocument>("demo");
            await database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));

            Console.WriteLine("1. Loading deterministic workshop data");
            await customers.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
            await orders.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
            await demo.DeleteManyAsync(FilterDefinition<BsonDocument>.Empty);
            await customers.InsertManyAsync(CreateCustomers());
            foreach (var batch in CreateOrders().Chunk(1000))
            {
                await orders.InsertManyAsync(batch);
            }
            Console.WriteLine($"Customers: {await customers.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty)}");
            Console.WriteLine($"Orders: {await orders.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty)}");

            Console.WriteLine("\n2. Comparing embedded and polymorphic models");
            await demo.InsertManyAsync(CreateModelingDocuments());
            Console.WriteLine($"Embedded documents: {await demo.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("_id", "DEMO-001"))}");
            Console.WriteLine($"Polymorphic documents: {await demo.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("orderId", "DEMO-002"))}");

            Console.WriteLine("\n3. Exploring status distribution");
            var statusCounts = await orders.Aggregate<BsonDocument>([
                new BsonDocument("$group", new BsonDocument { { "_id", "$status" }, { "count", new BsonDocument("$sum", 1) } }),
                new BsonDocument("$sort", new BsonDocument("count", -1))
            ]).ToListAsync();
            statusCounts.ForEach(document => Console.WriteLine(document));

            Console.WriteLine("\n4. Comparing a baseline with an ESR index");
            await orders.Indexes.DropAllAsync();
            var financeFilter = new BsonDocument { { "status", "delivered" }, { "total", new BsonDocument("$gte", 500) } };
            PrintExplain("baseline", await ExplainFindAsync(database, financeFilter, new BsonDocument("total", -1)));
            await orders.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
                new BsonDocument { { "status", 1 }, { "total", -1 } },
                new CreateIndexOptions { Name = "status_1_total_-1" }));
            PrintExplain("ESR index", await ExplainFindAsync(database, financeFilter, new BsonDocument("total", -1)));

            Console.WriteLine("\n5. Testing a distinct sort and range field");
            await orders.Indexes.DropAllAsync();
            var customerFilter = new BsonDocument { { "customerId", "C-1006" }, { "status", "shipped" }, { "total", new BsonDocument("$gte", 100) } };
            PrintExplain("baseline", await ExplainFindAsync(database, customerFilter, new BsonDocument("createdAt", -1)));
            await orders.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
                new BsonDocument { { "customerId", 1 }, { "status", 1 }, { "createdAt", -1 }, { "total", 1 } },
                new CreateIndexOptions { Name = "customer_status_date_total" }));
            PrintExplain("ESR index", await ExplainFindAsync(database, customerFilter, new BsonDocument("createdAt", -1)));

            Console.WriteLine("\n6. Aggregating the shipped-order leaderboard");
            var leaderboard = await orders.Aggregate<BsonDocument>([
                new BsonDocument("$match", new BsonDocument { { "status", "shipped" }, { "createdAt", new BsonDocument("$gte", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)) } }),
                new BsonDocument("$group", new BsonDocument { { "_id", "$customerId" }, { "totalRevenue", new BsonDocument("$sum", "$total") }, { "orderCount", new BsonDocument("$sum", 1) } }),
                new BsonDocument("$sort", new BsonDocument("totalRevenue", -1)),
                new BsonDocument("$limit", 5)
            ]).ToListAsync();
            leaderboard.ForEach(document => Console.WriteLine(document));
            Console.WriteLine("Success: compare the two explain tables and discuss which work each index removes.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static IEnumerable<BsonDocument> CreateCustomers() =>
        Enumerable.Range(0, 10).Select(index => new BsonDocument
        {
            { "_id", $"C-{1001 + index}" },
            { "name", $"Workshop Customer {index + 1}" },
            { "tier", new[] { "gold", "silver", "bronze" }[index % 3] },
            { "region", new[] { "eastus", "westus", "central" }[index % 3] }
        });

    private static IEnumerable<BsonDocument> CreateOrders()
    {
        string[] statuses = ["shipped", "pending", "delivered", "cancelled", "processing"];
        string[] skus = ["SKU-A1", "SKU-B2", "SKU-C3", "SKU-D4", "SKU-E5", "SKU-F6", "SKU-G7", "SKU-H8"];
        var start = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, 10000).Select(index => new BsonDocument
        {
            { "_id", $"O-{index + 1:D5}" },
            { "customerId", $"C-{1001 + index % 10}" },
            { "status", statuses[index * 7 % statuses.Length] },
            { "total", ((index * 7919) % 99900 + 100) / 100.0 },
            { "createdAt", start.AddDays(index * 37 % 730) },
            { "items", new BsonArray { new BsonDocument { { "sku", skus[index * 3 % skus.Length] }, { "qty", index % 5 + 1 } } } }
        });
    }

    private static IEnumerable<BsonDocument> CreateModelingDocuments() =>
    [
        new BsonDocument { { "_id", "DEMO-001" }, { "type", "embedded_order" }, { "customerId", "C-1001" }, { "total", 79.90 }, { "items", new BsonArray { new BsonDocument { { "sku", "BOOK-101" }, { "qty", 1 } }, new BsonDocument { { "sku", "BOOK-202" }, { "qty", 1 } } } } },
        new BsonDocument { { "_id", "DEMO-002" }, { "orderId", "DEMO-002" }, { "type", "order" }, { "customerId", "C-1001" }, { "total", 79.90 } },
        new BsonDocument { { "_id", "DEMO-002-1" }, { "orderId", "DEMO-002" }, { "type", "line_item" }, { "sku", "BOOK-101" }, { "qty", 1 } },
        new BsonDocument { { "_id", "DEMO-002-2" }, { "orderId", "DEMO-002" }, { "type", "line_item" }, { "sku", "BOOK-202" }, { "qty", 1 } }
    ];

    private static Task<BsonDocument> ExplainFindAsync(IMongoDatabase database, BsonDocument filter, BsonDocument sort) =>
        database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "explain", new BsonDocument { { "find", "orders" }, { "filter", filter }, { "sort", sort } } },
            { "verbosity", "executionStats" }
        });

    private static void PrintExplain(string label, BsonDocument explain)
    {
        var stats = explain.GetValue("executionStats", new BsonDocument()).AsBsonDocument;
        Console.WriteLine($"{label}: nReturned={stats.GetValue("nReturned", 0)}, docsExamined={stats.GetValue("totalDocsExamined", 0)}, keysExamined={stats.GetValue("totalKeysExamined", 0)}, timeMs={stats.GetValue("executionTimeMillis", 0)}");
    }

    private static string GetRequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Missing {name}. Run the Module 1 setup script first.");

    private sealed class AzureCliTokenHandler(TokenCredential credential) : IOidcCallback
    {
        private static readonly string[] Scopes = [DocumentDbScope];

        public OidcAccessToken GetOidcAccessToken(OidcCallbackParameters parameters, CancellationToken cancellationToken)
        {
            _ = parameters;
            var token = credential.GetToken(new TokenRequestContext(Scopes), cancellationToken);
            return new OidcAccessToken(token.Token, token.ExpiresOn - DateTimeOffset.UtcNow);
        }

        public async Task<OidcAccessToken> GetOidcAccessTokenAsync(OidcCallbackParameters parameters, CancellationToken cancellationToken)
        {
            _ = parameters;
            var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
            return new OidcAccessToken(token.Token, token.ExpiresOn - DateTimeOffset.UtcNow);
        }
    }
}