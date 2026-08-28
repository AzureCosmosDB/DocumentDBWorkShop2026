# Module 4: Search in Azure DocumentDB

**Duration:** 75 minutes

Notebook-first lab for Sales teams and customers. Use the `before` notebook in your language, then compare with the completed `after` notebook.

| Path | Purpose |
|---|---|
| `before/python/4_Search_DocumentDB.ipynb` | Python starter notebook. |
| `after/python/4_Search_DocumentDB.ipynb` | Python completed reference. |
| `before/csharp/4_Search_DocumentDB.ipynb` | C# starter notebook for .NET Interactive. |
| `after/csharp/4_Search_DocumentDB.ipynb` | C# completed reference. |
| `sample-data/documentdb_search_docs.json` | Shared small sample data. |

## Goals

Create and run Azure DocumentDB vector search, BM25 full-text search, fuzzy search, phrase search, and hybrid search with Reciprocal Rank Fusion.

## Prerequisites

- Azure DocumentDB cluster from Module 1.
- M30 or higher for DiskANN vector search.
- Full-text search enabled on the cluster; it is currently in gated preview.
- `DOCUMENTDB_CONNECTION_STRING` set in your notebook environment.
- Python: `pymongo`. C#: .NET Interactive; the notebook restores `MongoDB.Driver`.
