# Module 4 Hands-On Lab: Search in Azure DocumentDB

[Back to Module 4: Search in Azure DocumentDB](README.md)

Complete these exercises in order. Run commands in `mongosh` connected to your Azure DocumentDB cluster.

## Setup

Use a clean workshop database and load the sample content.

```javascript
use docdbworkshop

db.workshop_content.drop()

db.workshop_content.insertMany([
  {
    _id: "doc-search-001",
    title: "DiskANN vector indexing",
    category: "vector",
    body: "Azure DocumentDB supports DiskANN vector indexes for high recall semantic similarity search over embeddings stored with documents.",
    sku: "SEARCH-VEC-001",
    embedding: [0.92, 0.80, 0.18]
  },
  {
    _id: "doc-search-002",
    title: "BM25 keyword search",
    category: "full-text",
    body: "Azure DocumentDB full-text search ranks keyword matches with BM25 and exposes scores through searchScore metadata.",
    sku: "SEARCH-FTS-001",
    embedding: [0.20, 0.12, 0.94]
  },
  {
    _id: "doc-search-003",
    title: "Hybrid search with RRF",
    category: "hybrid",
    body: "Hybrid search combines BM25 keyword results with vector results and fuses the ranked lists using Reciprocal Rank Fusion.",
    sku: "SEARCH-HYB-001",
    embedding: [0.76, 0.70, 0.42]
  },
  {
    _id: "doc-search-004",
    title: "RAG grounding",
    category: "rag",
    body: "Retrieval augmented generation retrieves relevant chunks from Azure DocumentDB and grounds the model answer in that context.",
    sku: "RAG-PIPE-001",
    embedding: [0.82, 0.74, 0.36]
  },
  {
    _id: "doc-search-005",
    title: "Operational filtering",
    category: "filters",
    body: "Search applications often filter by status, tenant, region, stock, or category after the search stage narrows candidate documents.",
    sku: "SEARCH-FLT-001",
    embedding: [0.35, 0.30, 0.82]
  }
])
```

Confirm the load:

```javascript
db.workshop_content.countDocuments()
```

Expected result: `5`.

---

## Exercise 1: Create a Vector Index

Create a DiskANN index over the `embedding` field.

```javascript
db.runCommand({
  createIndexes: "workshop_content",
  indexes: [
    {
      name: "idx_embedding_diskann",
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

Notes:

- Use M30 or higher for DiskANN.
- Use `dimensions: 3` only for this lab's sample vectors.
- Use your embedding model's real dimension count in production, such as `1536` for `text-embedding-3-small`.
- Vectors must be stored as a `number[]` field to be indexed.

Run a semantic vector search:

```javascript
const semanticQueryVector = [0.90, 0.78, 0.22]

db.workshop_content.aggregate([
  {
    $search: {
      cosmosSearch: {
        path: "embedding",
        vector: semanticQueryVector,
        k: 3
      }
    }
  },
  {
    $project: {
      _id: 0,
      title: 1,
      category: 1,
      body: 1,
      score: { $meta: "searchScore" }
    }
  }
])
```

Record the top three titles.

Question: Why does this query return documents by embedding similarity instead of keyword overlap?

---

## Exercise 2: Create a Full-Text Search Index

Create a BM25 search index over the `body` field.

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

Search indexes build asynchronously. Inspect the index status:

```javascript
db.workshop_content.aggregate([
  { $listSearchIndexes: { name: "idx_body_fts" } }
])
```

Wait until the index is ready before running queries.

---

## Exercise 3: Run BM25 Keyword Search

Run a keyword query for `BM25 ranking`.

```javascript
db.workshop_content.aggregate([
  {
    $search: {
      index: "idx_body_fts",
      text: {
        query: "BM25 ranking",
        path: "body"
      }
    }
  },
  { $limit: 5 },
  {
    $project: {
      _id: 0,
      title: 1,
      category: 1,
      body: 1,
      score: { $meta: "searchScore" }
    }
  }
])
```

Question: Which document ranks first, and what exact terms helped it rank?

Add a downstream filter:

```javascript
db.workshop_content.aggregate([
  {
    $search: {
      index: "idx_body_fts",
      text: {
        query: "search",
        path: "body"
      }
    }
  },
  { $limit: 20 },
  { $match: { category: { $in: ["vector", "hybrid"] } } },
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

Question: Why does the `$match` come after `$search` instead of inside `$search`?

---

## Exercise 4: Run Fuzzy Search

Run a typo-tolerant query.

```javascript
db.workshop_content.aggregate([
  {
    $search: {
      index: "idx_body_fts",
      text: {
        query: "retrival augmentd genration",
        path: "body",
        fuzzy: { maxEdits: 1 }
      }
    }
  },
  { $limit: 5 },
  {
    $project: {
      _id: 0,
      title: 1,
      body: 1,
      score: { $meta: "searchScore" }
    }
  }
])
```

Question: When would you avoid fuzzy search even though it increases recall?

---

## Exercise 5: Run Phrase Search

Run a phrase query where word order matters.

```javascript
db.workshop_content.aggregate([
  {
    $search: {
      index: "idx_body_fts",
      phrase: {
        query: "Reciprocal Rank Fusion",
        path: "body",
        slop: 0
      }
    }
  },
  { $limit: 5 },
  {
    $project: {
      _id: 0,
      title: 1,
      body: 1,
      score: { $meta: "searchScore" }
    }
  }
])
```

Now increase `slop` to `3` and rerun.

Question: How does phrase search differ from a plain `text` query for the same words?

---

## Exercise 6: Run Hybrid Search with RRF

Hybrid search runs a keyword query and a vector query, then fuses the ranked lists. This keeps exact term matching and semantic matching in the same application search path.

Run the keyword side:

```javascript
const userQuery = "semantic retrieval for rag"

const keywordHits = db.workshop_content.aggregate([
  {
    $search: {
      index: "idx_body_fts",
      text: {
        query: userQuery,
        path: "body"
      }
    }
  },
  { $limit: 5 },
  {
    $project: {
      _id: 1,
      title: 1,
      source: "keyword",
      score: { $meta: "searchScore" }
    }
  }
]).toArray()
```

Run the vector side. In a real app, replace this sample vector with the embedding generated for `userQuery`.

```javascript
const queryVector = [0.84, 0.76, 0.32]

const vectorHits = db.workshop_content.aggregate([
  {
    $search: {
      cosmosSearch: {
        path: "embedding",
        vector: queryVector,
        k: 5
      }
    }
  },
  {
    $project: {
      _id: 1,
      title: 1,
      source: "vector",
      score: { $meta: "searchScore" }
    }
  }
]).toArray()
```

Fuse the two ranked lists with Reciprocal Rank Fusion.

```javascript
function rrf(lists, k = 60, topN = 5) {
  const scores = new Map()
  const titles = new Map()

  for (const list of lists) {
    list.forEach((doc, rank) => {
      const id = doc._id.toString()
      titles.set(id, doc.title)
      scores.set(id, (scores.get(id) || 0) + 1 / (k + rank + 1))
    })
  }

  return [...scores.entries()]
    .sort((a, b) => b[1] - a[1])
    .slice(0, topN)
    .map(([id, score]) => ({ _id: id, title: titles.get(id), rrfScore: score }))
}

rrf([keywordHits, vectorHits])
```

Question: Which results benefited from appearing in both lists?

---

## Exercise 7: Choose the Right Search Mode

For each query, choose vector, full-text, or hybrid search.

| User query | Recommended mode | Why |
|---|---|---|
| `SEARCH-HYB-001` | | |
| `typo tolernt retrival` | | |
| `content similar to grounding an answer with retrieved chunks` | | |
| `Reciprocal Rank Fusion` | | |
| `rag pipeline vector keyword exact identifiers` | | |

## Lab Success Check

- [ ] You created a vector index with `cosmosSearchOptions`.
- [ ] You used `$search.cosmosSearch` to run semantic vector retrieval.
- [ ] You created a BM25 search index with `createSearchIndexes`.
- [ ] You ran keyword, fuzzy, and phrase search with explicit index names.
- [ ] You combined keyword and vector results with RRF.
- [ ] You can explain why hybrid search is useful for RAG and application search.
