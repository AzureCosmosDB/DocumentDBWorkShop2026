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

Store chunks and embeddings in Azure DocumentDB, create vector and BM25 indexes, retrieve context with vector and hybrid search, and build a grounded prompt for a chat model.

## Prerequisites

- Azure DocumentDB cluster from Module 1.
- M30 or higher for DiskANN vector search.
- Full-text search enabled on the cluster; it is currently in gated preview.
- `DOCUMENTDB_CONNECTION_STRING` set in your notebook environment, or paste the connection string into Step 0.
- Python: `pymongo`. C#: .NET Interactive; the notebook restores `MongoDB.Driver`. Node.js: `mongodb` package, installed by the notebook if missing.
