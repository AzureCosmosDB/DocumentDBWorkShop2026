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

  return (await response.json()).data[0].embedding;
}

function fuseWithRrf(resultLists, rankConstant = 60, limit = 3) {
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
    .map(([id, score]) => ({ ...documents.get(id), rrfScore: score }));
}

async function main() {
  const clusterName = getRequiredEnvironmentVariable("DOCUMENTDB_CLUSTER_NAME");
  const azureOpenAiEndpoint = getRequiredEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
  const embeddingDeployment = getRequiredEnvironmentVariable(
    "AZURE_OPENAI_EMBEDDING_DEPLOYMENT"
  );
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
    let chunks = database.collection("rag_chunks");
    await database.command({ ping: 1 });
    console.log("Connected to Azure DocumentDB with Microsoft Entra ID.");

    const ragDocuments = [
      { _id: "rag-001", sourceId: "search-module", title: "Vector search", chunk: "Azure DocumentDB vector search uses the $search stage with the cosmosSearch operator to retrieve documents by embedding similarity.", url: "module-4-search", tags: ["vector", "search"] },
      { _id: "rag-002", sourceId: "search-module", title: "Full-text search", chunk: "Azure DocumentDB full-text search uses createSearchIndexes and the $search text operator to return BM25-ranked keyword matches.", url: "module-4-search", tags: ["full-text", "bm25"] },
      { _id: "rag-003", sourceId: "search-module", title: "Hybrid search", chunk: "Hybrid search runs BM25 keyword retrieval and vector retrieval, then combines ranked lists with Reciprocal Rank Fusion.", url: "module-4-search", tags: ["hybrid", "rrf"] },
      { _id: "rag-004", sourceId: "rag-module", title: "Grounded generation", chunk: "A RAG pipeline retrieves relevant chunks from Azure DocumentDB and includes them in the model prompt so the answer is grounded in current application data.", url: "module-5-rag", tags: ["rag", "generation"] }
    ];

    for (const document of ragDocuments) {
      document.embedding = await createEmbedding(
        credential,
        azureOpenAiEndpoint,
        embeddingDeployment,
        document.chunk
      );
    }

    await chunks.drop().catch((error) => {
      if (error.codeName !== "NamespaceNotFound") throw error;
    });
    chunks = database.collection("rag_chunks");
    await chunks.insertMany(ragDocuments);
    const embeddingDimensions = ragDocuments[0].embedding.length;
    console.log(`Loaded ${ragDocuments.length} chunks with ${embeddingDimensions}-dimension embeddings.`);

    await database.command({
      createIndexes: "rag_chunks",
      indexes: [{
        name: "idx_chunk_embedding_diskann",
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
        createSearchIndexes: "rag_chunks",
        indexes: [{
          name: "idx_chunk_fts",
          definition: {
            mappings: {
              dynamic: false,
              fields: { chunk: { type: "string" } }
            }
          }
        }]
      });
      console.log("Created vector and full-text retrieval indexes.");
    } catch (error) {
      if (error.code !== 115) throw error;
      fullTextSearchSupported = false;
      console.warn("Full-text search is not enabled for this cluster; continuing with vector-only retrieval.");
    }

    const question = "How does DocumentDB retrieve context for RAG?";
    const questionVector = await createEmbedding(
      credential,
      azureOpenAiEndpoint,
      embeddingDeployment,
      question
    );
    const vectorContext = await chunks.aggregate([
      { $search: { cosmosSearch: { path: "embedding", vector: questionVector, k: 3, lSearch: 40 } } },
      { $project: { _id: 1, title: 1, chunk: 1, url: 1, score: { $meta: "searchScore" } } }
    ]).toArray();
    const keywordContext = fullTextSearchSupported
      ? await chunks.aggregate([
        { $search: { index: "idx_chunk_fts", text: { query: question, path: "chunk" } } },
        { $limit: 3 },
        { $project: { _id: 1, title: 1, chunk: 1, url: 1, score: { $meta: "searchScore" } } }
      ]).toArray()
      : [];
    const hybridContext = fullTextSearchSupported
      ? fuseWithRrf([keywordContext, vectorContext])
      : vectorContext;

    const contextBlock = hybridContext
      .map((document, index) =>
        `[${index + 1}] ${document.title}\n${document.chunk}\nSource: ${document.url}`
      )
      .join("\n\n");
    const groundedPrompt = `You are a helpful assistant for an Azure DocumentDB workshop.
Answer the user's question using only the context below.
If the context does not contain the answer, say you do not know based on the provided context.

<context>
${contextBlock}
</context>

Question: ${question}`;

    console.log("\nGrounded prompt\n");
    console.log(groundedPrompt);
  } finally {
    await client.close();
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
