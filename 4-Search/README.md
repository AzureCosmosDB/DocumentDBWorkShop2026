# Module 4: Search in Azure DocumentDB

**Duration:** 75 minutes

Notebook-first lab for Sales teams and customers. Use the `before` notebook in your language, then compare with the completed `after` notebook.

| Path | Purpose |
|---|---|
| `before/python/4_Search_DocumentDB.ipynb` | Python runnable notebook. |
| `after/python/4_Search_DocumentDB.ipynb` | Python completed reference. |
| `before/csharp/4_Search_DocumentDB.ipynb` | C# runnable notebook for .NET Interactive. |
| `after/csharp/4_Search_DocumentDB.ipynb` | C# completed reference. |
| `before/nodejs/4_Search_DocumentDB.ipynb` | Node.js runnable notebook. |
| `after/nodejs/4_Search_DocumentDB.ipynb` | Node.js completed reference. |
| `sample-data/documentdb_search_docs.json` | Source records used by the notebooks before embeddings are generated. |

## Goals

Create embeddings with OpenAI, store them in Azure DocumentDB, create and run vector search, BM25 full-text search, fuzzy search, phrase search, and hybrid search with Reciprocal Rank Fusion.

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
