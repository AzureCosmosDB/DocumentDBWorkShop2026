# Module 4: Search in Azure DocumentDB

**Duration:** 75 minutes

This module adds application search patterns to the workshop. You will use Azure DocumentDB as the operational document store and search engine for vector search, BM25 full-text search, and hybrid retrieval.

## Learning Goals

- Create a DiskANN vector index with `cosmosSearch`.
- Run semantic vector search with the `$search` aggregation stage.
- Create a BM25 full-text search index with `createSearchIndexes`.
- Run keyword, fuzzy, and phrase searches with `$search`.
- Combine vector and keyword results with Reciprocal Rank Fusion (RRF) for hybrid search.
- Explain when each search mode is the right fit.

## Prerequisites

- An Azure DocumentDB cluster from [Module 1](../1-DocumentDB-Introduction-and-Cluster-Setup/cluster-setup.md).
- Cluster tier M30 or higher for DiskANN vector search.
- Full-text search enabled on the cluster. Full-text search is currently in gated preview.
- `mongosh` connected to the target cluster.
- The sample data in [sample-data/documentdb_search_docs.json](sample-data/documentdb_search_docs.json).

## Module Materials

1. Review the concepts below.
2. Complete the [Hands-On Lab](lab.md).
3. Record the result differences between vector, full-text, and hybrid search.

## Search Modes

| Mode | Best for | Main syntax |
|---|---|---|
| Vector search | Semantic similarity, paraphrases, recommendations, RAG retrieval | `$search` + `cosmosSearch` |
| Full-text search | Exact keyword relevance, typos, phrase/proximity search | `$search` + `text` or `phrase` |
| Hybrid search | Queries that need both semantic meaning and exact identifiers | Run vector + BM25, then fuse with RRF |

## Key Azure DocumentDB Syntax

### Vector Index

Azure DocumentDB vector search uses a regular index command with the `cosmosSearch` key type and `cosmosSearchOptions`.

```javascript
db.runCommand({
  createIndexes: "workshop_content",
  indexes: [
    {
      name: "idx_embedding_diskann",
      key: { embedding: "cosmosSearch" },
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

Production embeddings commonly use 1,536 dimensions with `text-embedding-3-small`. This lab uses 3-dimensional sample vectors so the search mechanics are easy to inspect in `mongosh`.

### Vector Query

Vector queries run through `$search` and the `cosmosSearch` operator.

```javascript
db.workshop_content.aggregate([
  {
    $search: {
      cosmosSearch: {
        path: "embedding",
        vector: [0.92, 0.80, 0.18],
        k: 3
      }
    }
  },
  {
    $project: {
      _id: 0,
      title: 1,
      category: 1,
      score: { $meta: "searchScore" }
    }
  }
])
```

### Full-Text Search Index

Azure DocumentDB full-text search uses `createSearchIndexes`, not `db.collection.createIndex({ field: "text" })`.

```javascript
db.runCommand({
  createSearchIndexes: "workshop_content",
  indexes: [
    {
      name: "idx_body_fts",
      definition: {
        mappings: {
          dynamic: false,
          fields: {
            body: { type: "string" }
          }
        }
      }
    }
  ]
})
```

### Full-Text Query

Rules for full-text search:

- `$search` is the first aggregation stage.
- Always specify `index: "<name>"`.
- Put `$limit` after `$search`.
- Put equality and range filters in downstream `$match` stages.

```javascript
db.workshop_content.aggregate([
  {
    $search: {
      index: "idx_body_fts",
      text: {
        query: "vector indexing",
        path: "body"
      }
    }
  },
  { $limit: 5 },
  {
    $project: {
      _id: 0,
      title: 1,
      score: { $meta: "searchScore" }
    }
  }
])
```

## Success Check

- [ ] You created a `cosmosSearch` vector index.
- [ ] You created a `createSearchIndexes` full-text index.
- [ ] You ran vector search and explained why it found semantic matches.
- [ ] You ran BM25, fuzzy, and phrase searches and explained the ranking behavior.
- [ ] You built a hybrid result set with RRF.
- [ ] You can choose between vector, full-text, and hybrid search for an application scenario.
