# Module 5 Hands-On Lab: RAG Pipeline with Azure DocumentDB

[Back to Module 5: RAG Pipeline with Azure DocumentDB](README.md)

Complete these exercises in order. Run database commands in `mongosh` connected to your Azure DocumentDB cluster. The final generation step is written as application pseudocode because the exact embedding and chat clients depend on your workshop environment.

## Setup

Use a dedicated collection for RAG chunks.

```javascript
use docdbworkshop

db.rag_chunks.drop()

db.rag_chunks.insertMany([
  {
    _id: "rag-001",
    sourceId: "search-module",
    title: "Vector search",
    chunk: "Azure DocumentDB vector search uses the $search stage with the cosmosSearch operator to retrieve documents by embedding similarity.",
    url: "module-4-search",
    tags: ["vector", "search"],
    embedding: [0.92, 0.80, 0.18]
  },
  {
    _id: "rag-002",
    sourceId: "search-module",
    title: "Full-text search",
    chunk: "Azure DocumentDB full-text search uses createSearchIndexes and the $search text operator to return BM25-ranked keyword matches.",
    url: "module-4-search",
    tags: ["full-text", "bm25"],
    embedding: [0.20, 0.12, 0.94]
  },
  {
    _id: "rag-003",
    sourceId: "search-module",
    title: "Hybrid search",
    chunk: "Hybrid search runs BM25 keyword retrieval and vector retrieval, then combines ranked lists with Reciprocal Rank Fusion.",
    url: "module-4-search",
    tags: ["hybrid", "rrf"],
    embedding: [0.76, 0.70, 0.42]
  },
  {
    _id: "rag-004",
    sourceId: "rag-module",
    title: "Grounded generation",
    chunk: "A RAG pipeline retrieves relevant chunks from Azure DocumentDB and includes them in the model prompt so the answer is grounded in current application data.",
    url: "module-5-rag",
    tags: ["rag", "generation"],
    embedding: [0.84, 0.73, 0.34]
  }
])
```

Confirm the load:

```javascript
db.rag_chunks.countDocuments()
```

Expected result: `4`.

---

## Exercise 1: Create Retrieval Indexes

Create a DiskANN vector index for semantic retrieval.

```javascript
db.runCommand({
  createIndexes: "rag_chunks",
  indexes: [
    {
      name: "idx_chunk_embedding_diskann",
      key: {
        embedding: "cosmosSearch"
      },
      cosmosSearchOptions: {
        kind: "vector-diskann",
        dimensions: 3,
        similarity: "COS",
        maxDegree: 32,
        lBuild: 64
      }
    }
  ]
})
```

Create a BM25 search index for keyword retrieval.

```javascript
db.runCommand({
  createSearchIndexes: "rag_chunks",
  indexes: [
    {
      name: "idx_chunk_fts",
      definition: {
        mappings: {
          dynamic: false,
          fields: {
            chunk: { type: "string" }
          }
        }
      }
    }
  ]
})
```

Inspect the search index status:

```javascript
db.rag_chunks.aggregate([
  { $listSearchIndexes: { name: "idx_chunk_fts" } }
])
```

---

## Exercise 2: Retrieve Context with Vector Search

In production, generate the query vector from the user's question. In this lab, use a sample vector near the RAG and hybrid chunks.

```javascript
const question = "How does DocumentDB retrieve context for RAG?"
const questionVector = [0.83, 0.74, 0.33]

const vectorContext = db.rag_chunks.aggregate([
  {
    $search: {
      cosmosSearch: {
        path: "embedding",
        vector: questionVector,
        k: 3
      }
    }
  },
  {
    $project: {
      _id: 1,
      title: 1,
      chunk: 1,
      url: 1,
      score: { $meta: "searchScore" }
    }
  }
]).toArray()

vectorContext
```

Question: Which chunks should be most useful to answer the question?

---

## Exercise 3: Retrieve Context with Hybrid Search

Hybrid retrieval is often better for RAG because user questions can mix natural language with exact terms such as `BM25`, `DiskANN`, `$search`, or `cosmosSearch`.

Run the BM25 side:

```javascript
const keywordContext = db.rag_chunks.aggregate([
  {
    $search: {
      index: "idx_chunk_fts",
      text: {
        query: question,
        path: "chunk"
      }
    }
  },
  { $limit: 3 },
  {
    $project: {
      _id: 1,
      title: 1,
      chunk: 1,
      url: 1,
      score: { $meta: "searchScore" }
    }
  }
]).toArray()
```

Fuse keyword and vector lists with RRF:

```javascript
function rrf(lists, k = 60, topN = 3) {
  const docs = new Map()
  const scores = new Map()

  for (const list of lists) {
    list.forEach((doc, rank) => {
      const id = doc._id.toString()
      docs.set(id, doc)
      scores.set(id, (scores.get(id) || 0) + 1 / (k + rank + 1))
    })
  }

  return [...scores.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, topN)
    .map(([id, score]) => ({ ...docs.get(id), rrfScore: score }))
}

const hybridContext = rrf([keywordContext, vectorContext])
hybridContext
```

Question: Did hybrid search pull in any chunk that vector-only retrieval missed or ranked lower?

---

## Exercise 4: Build the Grounded Prompt

Turn retrieved chunks into context for the model.

```javascript
const contextBlock = hybridContext
  .map((doc, i) => `[${i + 1}] ${doc.title}\n${doc.chunk}\nSource: ${doc.url}`)
  .join("\n\n")

const groundedPrompt = `You are a helpful assistant for an Azure DocumentDB workshop.
Answer the user's question using only the context below.
If the context does not contain the answer, say you do not know based on the provided context.

<context>
${contextBlock}
</context>

Question: ${question}`

print(groundedPrompt)
```

Question: What instruction prevents the model from answering from training data when the retrieved context is insufficient?

---

## Exercise 5: Add the Generation Call in Your App

In your application tier, replace `embed(question)` and `chatComplete(messages)` with your actual Azure OpenAI or Azure AI Foundry clients.

```javascript
async function answerWithDocumentDBRag(question) {
  const questionVector = await embed(question)

  const vectorHits = await db.collection("rag_chunks").aggregate([
    {
      $search: {
        cosmosSearch: {
          path: "embedding",
          vector: questionVector,
          k: 20
        }
      }
    },
    { $project: { title: 1, chunk: 1, url: 1, score: { $meta: "searchScore" } } }
  ]).toArray()

  const keywordHits = await db.collection("rag_chunks").aggregate([
    {
      $search: {
        index: "idx_chunk_fts",
        text: {
          query: question,
          path: "chunk"
        }
      }
    },
    { $limit: 20 },
    { $project: { title: 1, chunk: 1, url: 1, score: { $meta: "searchScore" } } }
  ]).toArray()

  const context = rrf([keywordHits, vectorHits], 60, 5)
    .map((doc, i) => `[${i + 1}] ${doc.title}\n${doc.chunk}\nSource: ${doc.url}`)
    .join("\n\n")

  return chatComplete([
    {
      role: "system",
      content: "Answer using only the provided Azure DocumentDB context. If the answer is not present, say you do not know based on the provided context."
    },
    {
      role: "user",
      content: `<context>\n${context}\n</context>\n\nQuestion: ${question}`
    }
  ])
}
```

Question: Why does this application store source text, metadata, and vectors together instead of writing vectors to a separate service?

---

## Exercise 6: Production Checklist

Review this checklist before adapting the lab pattern to a real application:

- Use real model-generated embeddings and set the vector index dimensions to the model's output dimension.
- Choose DiskANN for larger production datasets; tune HNSW or IVF only when their trade-offs fit your scale.
- Keep BM25 and vector indexes on the same collection when the same documents power both retrieval modes.
- Use explicit search index names in every `$search` query.
- Keep `$search` as the first aggregation stage.
- Apply equality and range filters in downstream `$match` stages for full-text queries.
- Keep per-query retrieval depth modest, commonly 20 to 100 candidates per retriever before fusion.
- Include citations or source metadata in the final prompt so answers can be traced to retrieved chunks.

## Lab Success Check

- [ ] You loaded RAG chunks into Azure DocumentDB.
- [ ] You created vector and full-text retrieval indexes.
- [ ] You retrieved chunks with vector search.
- [ ] You retrieved chunks with hybrid search.
- [ ] You assembled a grounded prompt.
- [ ] You can describe how this becomes a production RAG pipeline with real embedding and chat clients.
