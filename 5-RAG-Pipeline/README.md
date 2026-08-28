# Module 5: RAG Pipeline with Azure DocumentDB

**Duration:** 60 minutes

Notebook-first lab for building a retrieval-augmented generation pipeline on Azure DocumentDB. Use the `before` notebook in your language, then compare with the completed `after` notebook.

| Path | Purpose |
|---|---|
| `before/python/5_RAG_Pipeline_DocumentDB.ipynb` | Python runnable notebook. |
| `after/python/5_RAG_Pipeline_DocumentDB.ipynb` | Python completed reference. |
| `before/csharp/5_RAG_Pipeline_DocumentDB.ipynb` | C# runnable notebook for .NET Interactive. |
| `after/csharp/5_RAG_Pipeline_DocumentDB.ipynb` | C# completed reference. |
| `before/nodejs/5_RAG_Pipeline_DocumentDB.ipynb` | Node.js runnable notebook. |
| `after/nodejs/5_RAG_Pipeline_DocumentDB.ipynb` | Node.js completed reference. |

## Goals

Create embeddings with OpenAI, store chunks and embeddings in Azure DocumentDB, create vector and BM25 indexes, retrieve context with vector and hybrid search, and build a grounded prompt for a chat model.

## Prerequisites

- Azure DocumentDB cluster from Module 1.
- M30 or higher for DiskANN vector search.
- Full-text search enabled on the cluster; it is currently in gated preview.
- Azure DocumentDB connection string.
- OpenAI API key for embeddings.
- Python: `pymongo` and `openai`, installed by the notebook if missing.
- C#: .NET Interactive; the notebook restores `MongoDB.Driver`.
- Node.js: `mongodb` package, installed by the notebook if missing.

## How Participants Run It

1. Open the `before` notebook for Python, C#, or Node.js.
2. Paste the Azure DocumentDB connection string in Step 0, or set `DOCUMENTDB_CONNECTION_STRING`.
3. Paste the OpenAI API key in Step 0, or set `OPENAI_API_KEY`.
4. Run every cell in order.
5. Compare with the matching `after` notebook.
