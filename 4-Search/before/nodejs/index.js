const { AzureCliCredential } = require("@azure/identity");
const { MongoClient } = require("mongodb");

const documentDbScope = "https://ossrdbms-aad.database.windows.net/.default";
const azureOpenAiScope = "https://ai.azure.com/.default";

function getRequiredEnvironmentVariable(name) {
  const value = process.env[name];
  if (!value) {
    throw new Error(
      `Missing ${name}. Run ../../../1-DocumentDB-Introduction-and-Cluster-Setup/Set-LabEnvironment.ps1 first.`
    );
  }
  return value;
}

async function createEmbedding(credential, endpoint, deployment, text) {
  const token = await credential.getToken(azureOpenAiScope);
  const response = await fetch(`${endpoint.replace(/\/$/, "")}/openai/v1/embeddings`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token.token}`,
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ model: deployment, input: text })
  });

  if (!response.ok) {
    throw new Error(`Azure OpenAI returned ${response.status}: ${await response.text()}`);
  }

  const payload = await response.json();
  return payload.data[0].embedding;
}

function fuseWithRrf(resultLists, rankConstant = 60, limit = 5) {
  const scores = new Map();
  const documents = new Map();

  for (const results of resultLists) {
    results.forEach((document, rank) => {
      const id = document._id.toString();
      documents.set(id, document);
      scores.set(id, (scores.get(id) ?? 0) + 1 / (rankConstant + rank + 1));
    });
  }

  return [...scores.entries()]
    .sort((left, right) => right[1] - left[1])
    .slice(0, limit)
    .map(([id, score]) => ({
      id,
      title: documents.get(id).title,
      rrfScore: score
    }));
}

async function main() {
  const clusterName = getRequiredEnvironmentVariable("DOCUMENTDB_CLUSTER_NAME");
  const azureOpenAiEndpoint = getRequiredEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
  const embeddingDeployment = getRequiredEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT");
  const credential = new AzureCliCredential();

  const documentDbTokenCallback = async () => {
    const token = await credential.getToken(documentDbScope);
    return {
      accessToken: token.token,
      expiresInSeconds: Math.max(0, Math.floor((token.expiresOnTimestamp - Date.now()) / 1000))
    };
  };

  const client = new MongoClient(
    `mongodb+srv://${clusterName}.global.mongocluster.cosmos.azure.com/`,
    {
      tls: true,
      retryWrites: false,
      authMechanism: "MONGODB-OIDC",
      authMechanismProperties: {
        OIDC_CALLBACK: documentDbTokenCallback,
        ALLOWED_HOSTS: ["*.azure.com"]
      }
    }
  );

  try {
    await client.connect();
    const database = client.db("docdbworkshop");
    const collection = database.collection("workshop_content");
    await database.command({ ping: 1 });
    console.log("Connected to Azure DocumentDB with Microsoft Entra ID.");

    const sourceDocuments = [
      { _id: "doc-search-001", title: "DiskANN vector indexing", category: "vector", body: "Azure DocumentDB supports DiskANN vector indexes for high recall semantic similarity search over embeddings stored with documents.", sku: "SEARCH-VEC-001" },
      { _id: "doc-search-002", title: "BM25 keyword search", category: "full-text", body: "Azure DocumentDB full-text search ranks keyword matches with BM25 and exposes scores through searchScore metadata.", sku: "SEARCH-FTS-001" },
      { _id: "doc-search-003", title: "Hybrid search with RRF", category: "hybrid", body: "Hybrid search combines BM25 keyword results with vector results and fuses the ranked lists using Reciprocal Rank Fusion.", sku: "SEARCH-HYB-001" },
      { _id: "doc-search-004", title: "RAG grounding", category: "rag", body: "Retrieval augmented generation retrieves relevant chunks from Azure DocumentDB and grounds the model answer in that context.", sku: "RAG-PIPE-001" },
      { _id: "doc-search-005", title: "Operational filtering", category: "filters", body: "Search applications often filter by status, tenant, region, stock, or category after the search stage narrows candidate documents.", sku: "SEARCH-FLT-001" }
    ];

    for (const document of sourceDocuments) {
      document.embedding = await createEmbedding(
        credential,
        azureOpenAiEndpoint,
        embeddingDeployment,
        document.body
      );
    }

    await collection.drop().catch((error) => {
      if (error.codeName !== "NamespaceNotFound") throw error;
    });
    await collection.insertMany(sourceDocuments);
    const embeddingDimensions = sourceDocuments[0].embedding.length;
    console.log(`Loaded ${sourceDocuments.length} documents with ${embeddingDimensions}-dimension embeddings.`);

    await database.command({
      createIndexes: "workshop_content",
      indexes: [{
        name: "idx_embedding_diskann",
        key: { embedding: "cosmosSearch" },
        cosmosSearchOptions: {
          kind: "vector-diskann",
          dimensions: embeddingDimensions,
          similarity: "COS",
          maxDegree: 32,
          lBuild: 64
        }
      }]
    });
    let fullTextSearchSupported = true;
    try {
      await database.command({
        createSearchIndexes: "workshop_content",
        indexes: [{
          name: "idx_body_fts",
          definition: {
            mappings: {
              dynamic: false,
              fields: { body: { type: "string" } }
            }
          }
        }]
      });
      console.log("Created vector and full-text indexes.");
    } catch (error) {
      if (error.code !== 115) throw error;
      fullTextSearchSupported = false;
      console.warn("Full-text search is not enabled for this cluster; continuing with vector-only search.");
    }

    const searchText = "semantic retrieval for RAG";
    const queryVector = await createEmbedding(
      credential,
      azureOpenAiEndpoint,
      embeddingDeployment,
      searchText
    );
    const vectorResults = await collection.aggregate([
      { $search: { cosmosSearch: { path: "embedding", vector: queryVector, k: 3, lSearch: 40 } } },
      { $project: { _id: 1, title: 1, category: 1, score: { $meta: "searchScore" } } }
    ]).toArray();
    console.log("\nVector search");
    console.table(vectorResults);

    if (fullTextSearchSupported) {
      const bm25Results = await collection.aggregate([
        { $search: { index: "idx_body_fts", text: { query: "BM25 ranking", path: "body" } } },
        { $limit: 5 },
        { $project: { _id: 1, title: 1, score: { $meta: "searchScore" } } }
      ]).toArray();
      console.log("\nBM25 search");
      console.table(bm25Results);

      const fuzzyResults = await collection.aggregate([
        { $search: { index: "idx_body_fts", text: { query: "retrival augmentd genration", path: "body", fuzzy: { maxEdits: 1 } } } },
        { $limit: 5 },
        { $project: { _id: 1, title: 1, score: { $meta: "searchScore" } } }
      ]).toArray();
      console.log("\nFuzzy search");
      console.table(fuzzyResults);

      const phraseResults = await collection.aggregate([
        { $search: { index: "idx_body_fts", phrase: { query: "Reciprocal Rank Fusion", path: "body", slop: 0 } } },
        { $limit: 5 },
        { $project: { _id: 1, title: 1, score: { $meta: "searchScore" } } }
      ]).toArray();
      console.log("\nPhrase search");
      console.table(phraseResults);

      const hybridKeywordResults = await collection.aggregate([
        { $search: { index: "idx_body_fts", text: { query: searchText, path: "body" } } },
        { $limit: 5 },
        { $project: { _id: 1, title: 1 } }
      ]).toArray();
      const hybridVectorResults = await collection.aggregate([
        { $search: { cosmosSearch: { path: "embedding", vector: queryVector, k: 5, lSearch: 40 } } },
        { $project: { _id: 1, title: 1 } }
      ]).toArray();
      console.log("\nHybrid search with RRF");
      console.table(fuseWithRrf([hybridKeywordResults, hybridVectorResults]));
    }
  } finally {
    await client.close();
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
