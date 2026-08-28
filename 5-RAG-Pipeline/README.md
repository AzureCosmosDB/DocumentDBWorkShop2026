# Module 5: RAG Pipeline with Azure DocumentDB

**Duration:** 60 minutes

This module turns Azure DocumentDB search into a retrieval-augmented generation (RAG) pipeline. You will store chunks and embeddings in the same collection, retrieve relevant context with vector or hybrid search, and assemble a prompt that grounds a model answer in retrieved DocumentDB content.

## Learning Goals

- Model RAG chunks as DocumentDB documents.
- Store embeddings next to source text and metadata.
- Create vector and full-text search indexes on the same collection.
- Retrieve context with vector search and hybrid search.
- Build a grounded prompt from retrieved chunks.
- Understand how this module extends Module 4 search patterns into application generation.

## Prerequisites

- Completed [Module 4: Search in Azure DocumentDB](../4-Search/README.md), or equivalent vector and full-text search indexes.
- An Azure DocumentDB cluster from [Module 1](../1-DocumentDB-Introduction-and-Cluster-Setup/cluster-setup.md).
- Cluster tier M30 or higher for DiskANN vector search.
- Full-text search enabled on the cluster. Full-text search is currently in gated preview.
- `mongosh` connected to the target cluster.
- An embedding model in your application tier for production use.
- A chat completion model in your application tier for the final generation step.

## Module Materials

1. Complete the [Hands-On Lab](lab.md).
2. Compare vector-only retrieval with hybrid retrieval.
3. Review the final prompt and confirm the answer only uses retrieved context.

## RAG Flow

1. **Ingest:** Split source content into chunks.
2. **Embed:** Generate an embedding for each chunk.
3. **Store:** Save text, metadata, and embedding in Azure DocumentDB.
4. **Index:** Create a vector index and a BM25 search index on the same collection.
5. **Retrieve:** Search for the most relevant chunks.
6. **Generate:** Send the user question and retrieved context to the model.

## Collection Shape

Each chunk is a normal document:

```javascript
{
  _id: "rag-001",
  sourceId: "docdb-search-guide",
  title: "DocumentDB search overview",
  chunk: "Azure DocumentDB stores vector embeddings alongside JSON documents...",
  url: "https://learn.microsoft.com/azure/documentdb/",
  embedding: [0.82, 0.74, 0.36],
  tags: ["search", "rag"]
}
```

The key design point is that the original chunk text, metadata, and vector live together. This keeps retrieval simple and avoids a separate vector store.

## Retrieval Options

| Retriever | Use when |
|---|---|
| Vector-only | The question is natural language and semantic similarity is enough. |
| Full-text-only | The question contains exact identifiers, commands, error strings, or rare terms. |
| Hybrid | You want RAG retrieval that handles both paraphrases and exact identifiers. |

## Success Check

- [ ] You loaded RAG chunks into Azure DocumentDB.
- [ ] You created vector and BM25 indexes on one collection.
- [ ] You retrieved context with vector search.
- [ ] You retrieved context with hybrid search and RRF.
- [ ] You built a grounded prompt using retrieved chunks.
- [ ] You can explain why Azure DocumentDB can act as the operational store and retrieval store for a RAG application.
