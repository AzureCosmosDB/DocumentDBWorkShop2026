# Module 2: Data Modeling and Performance

Duration: 90 minutes

This module covers how to design document schemas for Azure DocumentDB and how to diagnose and fix slow queries using `explain()`, indexes, and the ESR rule.

## Module Materials

1. Work through the concepts and examples in the sections below.
2. Complete the [Hands-On Lab](lab.md) at each exercise checkpoint.
3. Review the success check at the end before the next module.

## What Participants Need

- An Azure DocumentDB cluster from Module 1 with a valid connection string.
- [DocumentDB for VS Code extension](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-documentdb) installed and connected to the cluster.
- The `docdbworkshop` database loaded with sample data (see [Dataset Setup](#dataset-setup) below).

## Learning Goals

- Understand when to embed documents versus when to reference them.
- Recognize common data modeling patterns for document databases.
- Run `explain("executionStats")` and read its output.
- Identify `COLLSCAN`, `SORT`, and `IXSCAN` stages and understand what they mean.
- Create single-field and compound indexes.
- Apply the ESR rule to design an effective compound index.
- Know how to hide an index for testing without dropping it.
- Follow a structured troubleshooting workflow for slow queries in production.

---

## Dataset Setup

This module uses an `orders` collection and a `customers` collection in the `docdbworkshop` database.

Connect to your cluster and run the following blocks **in order**.

### Load Customers

```javascript
use docdbworkshop

db.customers.insertMany([
  { _id: "C-1001", name: "James Anderson",    tier: "gold",    region: "eastus"  },
  { _id: "C-1002", name: "Patricia Williams", tier: "silver",  region: "westus"  },
  { _id: "C-1003", name: "Robert Johnson",    tier: "gold",    region: "eastus"  },
  { _id: "C-1004", name: "Linda Martinez",    tier: "bronze",  region: "eastus"  },
  { _id: "C-1005", name: "Michael Brown",     tier: "silver",  region: "westus"  },
  { _id: "C-1006", name: "Barbara Davis",     tier: "gold",    region: "central" },
  { _id: "C-1007", name: "William Garcia",    tier: "bronze",  region: "westus"  },
  { _id: "C-1008", name: "Elizabeth Wilson",  tier: "silver",  region: "eastus"  },
  { _id: "C-1009", name: "David Taylor",      tier: "gold",    region: "central" },
  { _id: "C-1010", name: "Susan Thomas",      tier: "bronze",  region: "westus"  }
])
```

### Load Orders (10,000 documents)

The following script generates 10,000 randomized orders in batches of 1,000. Run it using the DocumentDB for VS Code extension's integrated shell, or in a `mongosh` session — it takes about 10–20 seconds depending on network latency.

```javascript
use docdbworkshop

const statuses  = ["shipped", "pending", "delivered", "cancelled", "processing"]
const customers = ["C-1001","C-1002","C-1003","C-1004","C-1005",
                   "C-1006","C-1007","C-1008","C-1009","C-1010"]
const skus      = ["SKU-A1","SKU-B2","SKU-C3","SKU-D4","SKU-E5","SKU-F6","SKU-G7","SKU-H8"]

const start = new Date("2023-01-01").getTime()
const end   = new Date("2024-12-31").getTime()

let batch = []

for (let i = 0; i < 10000; i++) {
  const cid = customers[Math.floor(Math.random() * customers.length)]
  batch.push({
    customerId: cid,
    status:     statuses[Math.floor(Math.random() * statuses.length)],
    total:      Math.round(Math.random() * 99900 + 100) / 100,
    createdAt:  new Date(start + Math.random() * (end - start)),
    items: [{
      sku: skus[Math.floor(Math.random() * skus.length)],
      qty: Math.floor(Math.random() * 5) + 1
    }]
  })

  if (batch.length === 1000) {
    db.orders.insertMany(batch)
    batch = []
  }
}
if (batch.length > 0) db.orders.insertMany(batch)
print("Done. Total orders: " + db.orders.countDocuments())
```

### Verify

```javascript
db.customers.countDocuments()  // Expected: 10
db.orders.countDocuments()     // Expected: 10000
db.orders.findOne()
```

---

## Part 1: Data Modeling

### Documents, Collections, and Schemas

Azure DocumentDB stores data as BSON documents organized in collections. Unlike relational databases, there is no enforced schema — documents in the same collection can have different fields. In practice, you design a consistent structure that matches your application's data access patterns.

The most important design decision in a document database is how to relate pieces of data to each other: **embed** or **reference**.

---

### Embedding vs. Referencing

#### Embedding

Store related data inside the same document. The application retrieves the complete record in a single query.

```json
{
  "_id": "O-7001",
  "customerId": "C-1001",
  "status": "shipped",
  "total": 320.00,
  "createdAt": "2024-03-10T00:00:00Z",
  "items": [
    { "sku": "SKU-A1", "description": "Widget A", "qty": 2, "unitPrice": 160.00 },
    { "sku": "SKU-B2", "description": "Widget B", "qty": 1, "unitPrice": 45.00 }
  ]
}
```

**Use embedding when:**

- The related data is always read together with the parent document.
- The related data is owned exclusively by one parent (one-to-one or one-to-few).
- The sub-list is bounded — it will not grow to thousands of entries per document.

**Avoid embedding when:**

- The sub-document is shared across many parents (many-to-many).
- The sub-list is unbounded and could push the document past the 16 MB limit.
- The sub-data is updated frequently and independently of the parent.

---

#### Referencing

Store a foreign key and load the related document in a second query.

```json
// orders collection
{
  "_id": "O-7001",
  "customerId": "C-1001",
  "status": "shipped",
  "total": 320.00,
  "createdAt": "2024-03-10T00:00:00Z",
  "items": [{ "sku": "SKU-A1", "qty": 2 }]
}

// customers collection
{
  "_id": "C-1001",
  "name": "James Anderson",
  "tier": "gold",
  "region": "eastus"
}
```

**Use referencing when:**

- The related entity is shared or independently managed (for example, a product catalog used by many orders).
- The related entity changes frequently and you do not want to update many parent documents.
- You need to query the related entity on its own.

---

### Practical Patterns

#### Pattern 1: Extended Reference

Embed only the fields your application reads most often into the parent document, and keep the full authoritative record in its own collection. This eliminates the second query on the common read path while keeping the parent document lean.

```json
// products collection — authoritative source for current catalog data
{
  "_id":         "SKU-A1",
  "description": "Widget A",
  "category":    "hardware",
  "unitPrice":   160.00,
  "stock":       342
}

// orders collection — embeds a snapshot of the product fields needed at order time
{
  "_id":        "O-7001",
  "customerId": "C-1001",
  "status":     "shipped",
  "total":      365.00,
  "createdAt":  "2024-03-10T00:00:00Z",
  "items": [
    { "sku": "SKU-A1", "description": "Widget A", "qty": 2, "unitPrice": 160.00, "subtotal": 320.00 },
    { "sku": "SKU-B2", "description": "Widget B", "qty": 1, "unitPrice":  45.00, "subtotal":  45.00 }
  ]
}
```

**Trade-off:** If the product description or price changes in the `products` collection, the copy inside existing orders is not updated — which is intentional for line items. An order must preserve the price that was charged at the time of purchase, not the current catalog price. Fields that change over time and must be captured as a point-in-time snapshot are ideal candidates for this pattern.

#### Pattern 2: Bucket Pattern

Group related time-series or event records into a single parent document to reduce document count and index overhead. Each bucket covers a fixed time window.

```json
{
  "deviceId": "sensor-42",
  "hour":     "2024-09-12T14:00:00Z",
  "readings": [
    { "minute": 0, "temp": 22.1 },
    { "minute": 1, "temp": 22.3 },
    { "minute": 2, "temp": 22.0 }
  ],
  "count":   3,
  "avgTemp": 22.13
}
```

**When to use it:** IoT telemetry, clickstream events, audit logs — any scenario where millions of small events arrive per day and queries aggregate over time windows. One document per hour instead of one per reading reduces collection size dramatically and lowers index maintenance overhead.

#### Pattern 3: Polymorphic Pattern

Store documents of different shapes in the same collection, distinguished by a `type` field. This is useful when related entities have overlapping fields and are almost always queried together — keeping them in one collection avoids cross-collection joins and simplifies indexing.

A common e-commerce example splits an order into a **header** document and individual **line item** documents, all living in the same `orders` collection:

```json
// Order header — orderId duplicates _id so all documents in the order share the same query field
{
  "_id":        "O-7001",
  "orderId":    "O-7001",
  "type":       "order",
  "customerId": "C-1001",
  "status":     "shipped",
  "total":      365.00,
  "createdAt":  "2024-03-10T00:00:00Z"
}

// Line item — one document per SKU in the order
{
  "_id":      "O-7001-1",
  "type":     "line_item",
  "orderId":  "O-7001",
  "sku":      "SKU-A1",
  "description": "Widget A",
  "qty":      2,
  "unitPrice": 160.00,
  "subtotal":  320.00
}

// Line item — second SKU in the same order
{
  "_id":      "O-7001-2",
  "type":     "line_item",
  "orderId":  "O-7001",
  "sku":      "SKU-B2",
  "description": "Widget B",
  "qty":      1,
  "unitPrice":  45.00,
  "subtotal":   45.00
}
```

Because the header also carries `orderId`, all documents in the order share the same field and the query is a simple equality filter:

```javascript
// Returns the header + all line items for order O-7001
db.orders.find({ orderId: "O-7001" })
```

**When to use it:** When the number of line items per order is large or unbounded, embedding them would push documents toward the 16 MB limit. Splitting into header + items keeps each document small while a single compound index on `{ orderId: 1, type: 1 }` covers the most common access pattern without a cross-collection join.

**Trade-off:** Queries that need only the header must filter by `type: "order"` to avoid returning line items. The `type` field must be indexed if it appears frequently in query filters.

---

### Sharding

Azure DocumentDB supports **horizontal sharding** — distributing a collection across multiple physical nodes using a shard key. For most  workloads, sharding is not required. Consider it when:

- A collection exceeds **32 TB** of storage, or
- Your workload requires higher storage throughput than a single node provides.

In multi-node deployments, Azure DocumentDB offers two distribution modes:

- **Collection placement** — entire databases or collections are pinned to a specific shard. No shard key is defined; all documents in the collection live on the same node. This is the simplest model and works well when collections are naturally isolated from each other.
- **Collection sharding** — a single collection is distributed across multiple nodes based on a shard key field. Queries that include the shard key are routed to the relevant shard; queries without it are broadcast to all shards.

> Sharding configuration is outside the scope of this module.

---

## Part 2: Performance Troubleshooting Workflow

Fixing a slow query is more effective when you follow a consistent workflow. This section describes the recommended sequence from detection to validation.

### Step 1 — Find Slow Queries with Diagnostic Logs

Before running `explain()`, you need to know *which* queries to investigate. Enable diagnostic logs on your cluster and route them to an **Azure Log Analytics workspace**.

Setup instructions: [Monitor Azure DocumentDB with Diagnostic Logs](https://learn.microsoft.com/azure/documentdb/how-to-monitor-diagnostics-logs)

Once logs are flowing, use the following KQL query in Log Analytics to surface the slowest queries:

```kql
VCoreMongoRequests
| where DurationMs > 1000
| project TimeGenerated, DatabaseName, CollectionName,
          OperationName, DurationMs, PiiCommandText
| order by DurationMs desc
| take 20
```

Key fields:

| Field | What it tells you |
|---|---|
| `DurationMs` | Query execution time in milliseconds |
| `OperationName` | Operation type: `find`, `aggregate`, `update`, etc. |
| `CollectionName` | Which collection to investigate |
| `PiiCommandText` | The exact command executed |

> Set the `DurationMs` threshold based on your application's latency requirements and SLA. There is no universal value — a threshold that is too low generates noise; one that is too high misses real problems.

---

### Step 2 — Reproduce with `explain()`

Copy the `PiiCommandText` value from Log Analytics, adapt it to run with `explain("executionStats")`, and execute it directly against the database.

---

### Step 3 — Read the Output, Identify the Problem

Look for:

- A `COLLSCAN` stage — no index was used.
- A `SORT` stage after `FETCH` or `COLLSCAN` — in-memory sort.
- `totalDocsExamined` significantly larger than `nReturned` — low selectivity.

---

### Step 4 — Create or Adjust the Index

Apply the ESR rule (see Part 4) to design a compound index, create it, and re-run `explain()` to confirm the improvement.

---

### Step 5 — Validate in Production

After deploying the index, re-run the same KQL query in Log Analytics. Confirm that `DurationMs` for the affected query drops to the expected range.

---

## Part 3: Reading Query Explain Output

### How to Run `explain()`

Append `.explain("executionStats")` to any `find()`, or prepend it to an `aggregate()`:

```javascript
db.orders.find({ status: "shipped" }).explain("executionStats")

db.orders.explain("executionStats").aggregate([
  { $match: { status: "shipped" } },
  { $sort: { createdAt: -1 } }
])
```

| Verbosity mode | What it does |
|---|---|
| `"queryPlanner"` | Returns the winning plan without running the query. |
| `"executionStats"` | Runs the query and returns execution statistics. Use this for performance work. |
| `"allPlansExecution"` | Runs all candidate plans and returns statistics for each. |

---

### Key Fields

| Field | What it means |
|---|---|
| `nReturned` | Documents returned to the client. |
| `totalKeysExamined` | Index entries the engine scanned. |
| `totalDocsExamined` | Full documents loaded from storage. |
| `executionTimeMillis` | Total query time in milliseconds. |
| `winningPlan.stage` | The top-level execution strategy. |

**The efficiency ratio:**

$$\text{Efficiency} = \frac{\text{nReturned}}{\text{totalDocsExamined}}$$

A ratio close to **1.0** is ideal. A ratio of `18 / 333,000` means the engine loaded 333,000 documents to return 18 — a 99.99% waste that scales linearly with collection growth.

---

### Execution Stages

The table below covers the most common stages. DocumentDB supports additional stages — these are the ones you will encounter most often during performance work.

| Stage | Meaning | Signal |
|---|---|---|
| `COLLSCAN` | Full collection scan — reads every document. | Missing index; create one. |
| `IXSCAN` | Index scan — reads only index entries matching the filter. | Expected good path. |
| `FETCH` | Loads full documents after an index scan. | Normal; eliminate only with covered queries. |
| `SORT` | In-memory sort after document load. | Eliminate by including the sort field in a compound index. |

---

### Reading a Bad Plan — Live Demo

Drop any existing non-`_id` indexes so the baseline has no index support:

```javascript
db.orders.dropIndexes()
```

Run the baseline explain:

```javascript
db.orders.find(
  {
    status:     "shipped",
    customerId: "C-1001",
    createdAt:  { $gte: ISODate("2024-01-01") }
  }
).sort({ createdAt: -1 }).explain("executionStats")
```

```json
{
  "explainVersion": 2,
  "command": "db.runCommand({explain: { 'find': 'orders', 'filter': { 'status': 'shipped', 'customerId': 'C-1001', 'createdAt': { '$gte': DateTime('2024-01-01 0:00:00.0 +00:00:00') } }, 'sort': { 'createdAt': -1 } }})",
  "explainCommandPlanningTimeMillis": 13.035,
  "explainCommandExecTimeMillis": 3.064,
  "instanceName": "instanceName",
  "queryPlanner": {
    "namespace": "docdbworkshop.orders",
    "winningPlan": {
      "stage": "SORT",
      "startupCost": 130.19,
      "totalCost": 130.2,
      "sortKeysCount": 1,
      "sortKey": [
        {
          "createdAt": -1
        }
      ],
      "estimatedTotalKeysExamined": 4,
      "inputStage": {
        "stage": "FETCH",
        "ns": "docdbworkshop.orders",
        "startupCost": 3.08,
        "totalCost": 130.14,
        "runtimeFilterSet": [
          {
            "status": {
              "$eq": "shipped"
            }
          },
          {
            "customerId": {
              "$eq": "C-1001"
            }
          },
          {
            "createdAt": {
              "$gte": {
                "$date": "2024-01-01T00:00:00Z"
              }
            }
          }
        ],
        "estimatedTotalKeysExamined": 4,
        "inputStage": {
          "stage": "IXSCAN",
          "indexName": "_id_",
          "isBitmap": true,
          "indexUsage": {},
          "startupCost": 0,
          "totalCost": 3.08,
          "estimatedTotalKeysExamined": 105
        }
      }
    },
    "indexCosts": [
      {
        "namespace": "docdbworkshop.orders",
        "costs": [
          {
            "indexName": "_id_",
            "startupCost": 0.287,
            "totalCost": 3.075,
            "selectivity": 0.005,
            "estimatedPercentIndexPagesLoaded": 1.59,
            "estimatedTotalIndexEntries": 20979,
            "boundarySelectivity": 0.005
          }
        ]
      }
    ]
  },
  "executionStats": {
    "nReturned": 107,
    "executionTimeMillis": 3.01,
    "executionStartAtTimeMillis": 3.006,
    "totalDocsExamined": 107,
    "totalKeysExamined": 107,
    "executionStages": {
      "stage": "SORT",
      "nReturned": 107,
      "executionTimeMillis": 3.01,
      "executionStartAtTimeMillis": 3.006,
      "totalDocsExamined": 107,
      "totalKeysExamined": 107,
      "sortMethod": "quicksort",
      "totalDataSizeSortedBytesEstimate": 45,
      "numBlocksFromCache": 327,
      "inputStage": {
        "stage": "FETCH",
        "nReturned": 107,
        "executionTimeMillis": 2.873,
        "executionStartAtTimeMillis": 0.427,
        "totalDocsExamined": 10000,
        "totalKeysExamined": 107,
        "exactBlocksRead": 259,
        "totalDocsRemovedByRuntimeFilter": 9893,
        "numBlocksFromCache": 321,
        "inputStage": {
          "stage": "IXSCAN",
          "nReturned": 10000,
          "executionTimeMillis": 0.355,
          "executionStartAtTimeMillis": 0.355,
          "indexName": "_id_",
          "indexUsage": {},
          "totalKeysExamined": 10000,
          "numBlocksFromCache": 62
        }
      }
    }
  },
  "ok": 1
}
```

Red flags to call out in the output:

- `IXSCAN` on `_id_`: the optimizer picked the only available index, but it is scanning all 10,000 entries just to locate documents — functionally equivalent to a `COLLSCAN`. Without a purpose-built index, any scan of the full collection falls into this category.
- `totalDocsRemovedByRuntimeFilter: 9893`: the query filters (`status`, `customerId`, `createdAt`) were not evaluated inside the index. The engine loaded 10,000 documents into memory and discarded 9,893 of them at runtime. This is the key signal that no selective index exists for the filter fields.
- `SORT` stage on top: with no index to provide ordering, the 107 matching documents are sorted in memory after the scan completes.
- `totalDocsExamined: 10000` vs `nReturned: 107`: the engine touched every document to return roughly 1% of the collection.

> The query finishes in milliseconds at 10,000 documents. At 10 million orders the same plan would take seconds and consume significant CPU on every request.

---

### Aggregation Pipelines and Performance

An **aggregation pipeline** is a sequence of stages that transforms documents as they flow through it. Each stage receives the output of the previous one. Common stages include `$match` (filter), `$group` (aggregate), `$sort`, `$project` (shape output), and `$lookup` (join another collection).

```javascript
// Find the total revenue and order count per customer, for shipped orders only
db.orders.aggregate([
  { $match:  { status: "shipped" } },
  { $group:  { _id: "$customerId", revenue: { $sum: "$total" }, orders: { $sum: 1 } } },
  { $sort:   { revenue: -1 } },
  { $limit:  5 }
])
```

For performance analysis, run `explain()` on the pipeline. The syntax is slightly different from `find()` — prepend `.explain()` to `.aggregate()`:

```javascript
db.orders.explain("executionStats").aggregate([
  { $match:  { status: "shipped" } },
  { $group:  { _id: "$customerId", revenue: { $sum: "$total" }, orders: { $sum: 1 } } },
  { $sort:   { revenue: -1 } },
  { $limit:  5 }
])
```

```json
{
  "explainVersion": 2,
  "command": "db.runCommand({explain: { 'aggregate': 'orders', 'pipeline': [{ '$match': { 'status': 'shipped' } }, { '$group': { '_id': '$customerId', 'revenue': { '$sum': '$total' }, 'orders': { '$sum': 1 } } }, { '$sort': { 'revenue': -1 } }, { '$limit': 5 }], 'cursor': {} }})",
  "explainCommandPlanningTimeMillis": 27.398,
  "explainCommandExecTimeMillis": 4.276,
  "instanceName": "instanceName",
  "stages": [
    {
      "$cursor": {
        "queryPlanner": {
          "winningPlan": {
            "stage": "COLLSCAN",
            "startupCost": 0,
            "totalCost": 438.17,
            "runtimeFilterSet": [
              {
                "status": {
                  "$eq": "shipped"
                }
              }
            ],
            "estimatedTotalKeysExamined": 1667
          }
        },
        "executionStats": {
          "nReturned": 1983,
          "executionTimeMillis": 3.126,
          "executionStartAtTimeMillis": 0.015,
          "totalDocsExamined": 10000,
          "totalKeysExamined": 1983,
          "executionStages": {
            "stage": "COLLSCAN",
            "nReturned": 1983,
            "executionTimeMillis": 3.126,
            "executionStartAtTimeMillis": 0.015,
            "totalDocsExamined": 10000,
            "totalKeysExamined": 1983,
            "totalDocsRemovedByRuntimeFilter": 8017,
            "numBlocksFromCache": 259
          }
        }
      }
    },
    {
      "$group": {
        "queryPlanner": {
          "winningPlan": {
            "stage": "GROUP",
            "startupCost": 450.67,
            "totalCost": 488.18,
            "aggStrategy": "Hashed",
            "estimatedTotalKeysExamined": 1667
          }
        },
        "executionStats": {
          "nReturned": 10,
          "executionTimeMillis": 4.17,
          "executionStartAtTimeMillis": 4.158,
          "totalDocsExamined": 10,
          "totalKeysExamined": 10,
          "executionStages": {
            "stage": "GROUP",
            "nReturned": 10,
            "executionTimeMillis": 4.17,
            "executionStartAtTimeMillis": 4.158,
            "totalDocsExamined": 10,
            "totalKeysExamined": 10,
            "numBlocksFromCache": 259
          }
        }
      }
    },
    {
      "$sort": {
        "queryPlanner": {
          "winningPlan": {
            "stage": "SORT",
            "startupCost": 536.7,
            "totalCost": 540.87,
            "sortKeysCount": 1,
            "sortKey": [
              {
                "revenue": -1
              }
            ],
            "estimatedTotalKeysExamined": 1667,
            "inputStage": {
              "stage": "PROJECTION_DEFAULT",
              "startupCost": 450.67,
              "totalCost": 509.02,
              "estimatedTotalKeysExamined": 1667
            }
          }
        },
        "executionStats": {
          "nReturned": 5,
          "executionTimeMillis": 4.187,
          "executionStartAtTimeMillis": 4.186,
          "totalDocsExamined": 5,
          "totalKeysExamined": 5,
          "executionStages": {
            "stage": "SORT",
            "nReturned": 5,
            "executionTimeMillis": 4.187,
            "executionStartAtTimeMillis": 4.186,
            "totalDocsExamined": 5,
            "totalKeysExamined": 5,
            "sortMethod": "quicksort",
            "totalDataSizeSortedBytesEstimate": 26,
            "numBlocksFromCache": 259,
            "inputStage": {
              "stage": "PROJECTION_DEFAULT",
              "nReturned": 10,
              "executionTimeMillis": 4.175,
              "executionStartAtTimeMillis": 4.16,
              "totalDocsExamined": 10,
              "totalKeysExamined": 10,
              "numBlocksFromCache": 259
            }
          }
        }
      }
    },
    {
      "$limit": {
        "queryPlanner": {
          "winningPlan": {
            "stage": "LIMIT",
            "startupCost": 536.7,
            "totalCost": 536.72,
            "estimatedTotalKeysExamined": 5
          }
        },
        "executionStats": {
          "nReturned": 5,
          "executionTimeMillis": 4.188,
          "executionStartAtTimeMillis": 4.187,
          "totalDocsExamined": 5,
          "totalKeysExamined": 5,
          "executionStages": {
            "stage": "LIMIT",
            "nReturned": 5,
            "executionTimeMillis": 4.188,
            "executionStartAtTimeMillis": 4.187,
            "totalDocsExamined": 5,
            "totalKeysExamined": 5,
            "numBlocksFromCache": 259
          }
        }
      }
    }
  ],
  "ok": 1
}
```

Reading the output stage by stage:

- **`$cursor` → `COLLSCAN`**: no index exists for `status`, so the first stage reads all 10,000 documents. `totalDocsRemovedByRuntimeFilter: 8017` means the `status: "shipped"` filter was applied in memory after loading every document — not inside an index. Only 1,983 documents survive to the next stage.
- **`$group`**: in-memory hash aggregation over the 1,983 survivors. Produces 10 groups (one per `customerId`) — this stage itself is fast because the input is already small.
- **`$sort` / `$limit`**: sorts the 10 groups and returns 5.

The bottleneck is entirely in the first stage. An index on `{ status: 1 }` would let `$match` use `IXSCAN`, reducing the input to ~2,000 documents before any grouping happens and eliminating the `totalDocsRemovedByRuntimeFilter` waste.

The `$group` and `$sort` stages are always in-memory — there is no index that can pre-aggregate or pre-sort arbitrary expressions. The goal is to make the first `$cursor` stage as selective as possible so downstream stages work on the smallest possible dataset.

---

## Part 4: Indexes and the ESR Rule

### Single-Field Index

Create a single-field index on `status`:

```javascript
db.orders.createIndex({ status: 1 })
```

Re-run the same query with explain:

```javascript
db.orders.find(
  {
    status:     "shipped",
    customerId: "C-1001",
    createdAt:  { $gte: ISODate("2024-01-01") }
  }
).sort({ createdAt: -1 }).explain("executionStats")
```

```json
{
  "explainVersion": 2,
  "command": "db.runCommand({explain: { 'find': 'orders', 'filter': { 'status': 'shipped', 'customerId': 'C-1001', 'createdAt': { '$gte': DateTime('2024-01-01 0:00:00.0 +00:00:00') } }, 'sort': { 'createdAt': -1 } }})",
  "explainCommandPlanningTimeMillis": 29.672,
  "explainCommandExecTimeMillis": 1.195,
  "instanceName": "instanceName",
  "queryPlanner": {
    "namespace": "docdbworkshop.orders",
    "winningPlan": {
      "stage": "SORT",
      "startupCost": 141.71,
      "totalCost": 142.75,
      "sortKeysCount": 1,
      "sortKey": [
        {
          "createdAt": -1
        }
      ],
      "estimatedTotalKeysExamined": 417,
      "inputStage": {
        "stage": "FETCH",
        "ns": "docdbworkshop.orders",
        "startupCost": 0.1,
        "totalCost": 122.52,
        "runtimeFilterSet": [
          {
            "customerId": {
              "$eq": "C-1001"
            }
          },
          {
            "createdAt": {
              "$gte": {
                "$date": "2024-01-01T00:00:00Z"
              }
            }
          }
        ],
        "estimatedTotalKeysExamined": 417,
        "inputStage": {
          "stage": "IXSCAN",
          "indexName": "status_1",
          "isBitmap": true,
          "indexUsage": {
            "indexKeyString": "{\"status\": 1}",
            "isMultiKey": false,
            "bounds": [
              "[\"status\": [\"shipped\", \"shipped\"]]"
            ]
          },
          "startupCost": 0,
          "totalCost": 0,
          "indexFilterSet": [
            {
              "status": {
                "$eq": "shipped"
              }
            }
          ],
          "estimatedTotalKeysExamined": 100
        }
      }
    },
    "indexCosts": [
      {
        "namespace": "docdbworkshop.orders",
        "costs": [
          {
            "indexName": "_id_",
            "startupCost": 0.285,
            "totalCost": 201.285,
            "selectivity": 1,
            "correlation": 0.75,
            "estimatedPercentIndexPagesLoaded": 100,
            "estimatedTotalIndexEntries": 10000,
            "boundarySelectivity": 1
          }
        ]
      }
    ]
  },
  "executionStats": {
    "nReturned": 107,
    "executionTimeMillis": 1.079,
    "executionStartAtTimeMillis": 1.075,
    "totalDocsExamined": 107,
    "totalKeysExamined": 107,
    "executionStages": {
      "stage": "SORT",
      "nReturned": 107,
      "executionTimeMillis": 1.079,
      "executionStartAtTimeMillis": 1.075,
      "totalDocsExamined": 107,
      "totalKeysExamined": 107,
      "sortMethod": "quicksort",
      "totalDataSizeSortedBytesEstimate": 45,
      "numBlocksFromCache": 266,
      "inputStage": {
        "stage": "FETCH",
        "nReturned": 107,
        "executionTimeMillis": 0.947,
        "executionStartAtTimeMillis": 0.217,
        "totalDocsExamined": 1983,
        "totalKeysExamined": 107,
        "exactBlocksRead": 258,
        "totalDocsRemovedByRuntimeFilter": 1876,
        "numBlocksFromCache": 260,
        "inputStage": {
          "stage": "IXSCAN",
          "nReturned": 1983,
          "executionTimeMillis": 0.177,
          "executionStartAtTimeMillis": 0.176,
          "indexName": "status_1",
          "indexUsage": {
            "scanLoops": 1983,
            "scanType": "regular",
            "scanKeys": [
              "key 1: [(isInequality: false, estimatedEntryCount: 1983)]"
            ]
          },
          "totalKeysExamined": 1983,
          "numBlocksFromCache": 2
        }
      }
    }
  },
  "ok": 1
}
```

The plan now uses `IXSCAN` on `status_1` and execution dropped from 3ms to 1ms — a real improvement. But look closer:

- `totalDocsRemovedByRuntimeFilter: 1876`: the index narrowed the scan to the ~1,983 "shipped" orders, but `customerId` and `createdAt` were still applied as runtime filters after loading those documents. 1,876 of them were discarded in memory.
- `totalDocsExamined: 1983` vs `nReturned: 107`: the index helped with `status`, but the other two filter fields are doing no work inside the index.
- `SORT` stage still present: the index provides no ordering for `createdAt`, so the 107 matching documents are sorted in memory.

The single-field index solved one problem and left two others open.

---

### The ESR Rule for Compound Indexes

> **E**quality fields first → **S**ort field in the middle → **R**ange fields last

Apply it to the query:

| Field | Role | ESR position | Direction |
|---|---|---|---|
| `customerId` | Equality — high cardinality (10 customers, ~1,000 orders each) | 1st | `1` |
| `status` | Equality — low cardinality (5 values) | 2nd | `1` |
| `createdAt` | Sort **and** Range (`$gte`) | 3rd | `-1` (matches sort direction) |

> **Why `customerId` before `status`?**
> `customerId` eliminates ~90% of the collection immediately (only 1/10 of orders belong to a given customer). `status` eliminates only ~80% (1 of 5 values). The more selective equality field always goes first.

> **Why `createdAt: -1`?**
> The sort direction in the query is `{ createdAt: -1 }`. The index direction must match (or be the complete reverse) so the engine walks the index in order and skips the in-memory sort.

Drop the single-field index and create the ESR compound index:

```javascript
db.orders.dropIndex("status_1")

db.orders.createIndex({ customerId: 1, status: 1, createdAt: -1 })
```

Re-run explain:

```javascript
db.orders.find(
  {
    status:     "shipped",
    customerId: "C-1001",
    createdAt:  { $gte: ISODate("2024-01-01") }
  }
).sort({ createdAt: -1 }).explain("executionStats")
```

```json
{
  "explainVersion": 2,
  "command": "db.runCommand({explain: { 'find': 'orders', 'filter': { 'status': 'shipped', 'customerId': 'C-1001', 'createdAt': { '$gte': DateTime('2024-01-01 0:00:00.0 +00:00:00') } }, 'sort': { 'createdAt': -1 } }})",
  "explainCommandPlanningTimeMillis": 0.97,
  "explainCommandExecTimeMillis": 0.31,
  "instanceName": "instanceName",
  "queryPlanner": {
    "namespace": "docdbworkshop.orders",
    "winningPlan": {
      "stage": "FETCH",
      "ns": "docdbworkshop.orders",
      "startupCost": 0,
      "totalCost": 2.01,
      "estimatedTotalKeysExamined": 417,
      "inputStage": {
        "stage": "IXSCAN",
        "ns": "docdbworkshop.orders",
        "indexName": "customerId_1_status_1_createdAt_-1",
        "direction": "Forward",
        "indexUsage": {
          "indexKeyString": "{\"customerId\": 1,\"status\": 1,\"createdAt\": -1}",
          "isMultiKey": false,
          "bounds": [
            "[\"customerId\": [\"C-1001\", \"C-1001\"], \"status\": [\"shipped\", \"shipped\"], \"createdAt\": DESC[{ \"$date\" : \"2024-01-01T00:00:00Z\" }, { \"$date\" : \"292278994-08-17T07:12:55.807Z\" }]]"
          ]
        },
        "startupCost": 0,
        "totalCost": 2.01,
        "hasOrderBy": true,
        "indexFilterSet": [
          {
            "status": {
              "$eq": "shipped"
            }
          },
          {
            "customerId": {
              "$eq": "C-1001"
            }
          },
          {
            "createdAt": {
              "$gte": {
                "$date": "2024-01-01T00:00:00Z"
              }
            }
          }
        ],
        "estimatedTotalKeysExamined": 417
      }
    },
    "indexCosts": [
      {
        "namespace": "docdbworkshop.orders",
        "costs": [
          {
            "indexName": "_id_",
            "startupCost": 0.285,
            "totalCost": 201.285,
            "selectivity": 1,
            "correlation": 0.75,
            "estimatedPercentIndexPagesLoaded": 100,
            "estimatedTotalIndexEntries": 10000,
            "boundarySelectivity": 1
          }
        ]
      }
    ]
  },
  "executionStats": {
    "nReturned": 107,
    "executionTimeMillis": 0.19,
    "executionStartAtTimeMillis": 0.082,
    "totalDocsExamined": 107,
    "totalKeysExamined": 107,
    "executionStages": {
      "stage": "FETCH",
      "nReturned": 107,
      "executionTimeMillis": 0.19,
      "executionStartAtTimeMillis": 0.082,
      "totalKeysExamined": 107,
      "numBlocksFromCache": 133,
      "inputStage": {
        "stage": "IXSCAN",
        "nReturned": 107,
        "executionTimeMillis": 0.19,
        "executionStartAtTimeMillis": 0.082,
        "indexName": "customerId_1_status_1_createdAt_-1",
        "indexUsage": {
          "scanLoops": 108,
          "scanType": "ordered",
          "highKeyEligiblePages": 1,
          "scanKeys": [
            "key 1: [(isInequality: true, estimatedEntryCount: 107)]"
          ]
        },
        "totalKeysExamined": 107,
        "numBlocksFromCache": 133
      }
    }
  },
  "ok": 1
}
```

What the output confirms:

- No `SORT` stage — the winning plan goes straight from `IXSCAN` to `FETCH`. The engine did not need to sort documents in memory because the index already returns entries in the required order (`createdAt` descending).
- All three filters in `indexFilterSet` — `status`, `customerId`, and `createdAt` are all evaluated inside the index. No `runtimeFilterSet`, no `totalDocsRemovedByRuntimeFilter`.
- `totalKeysExamined: 107` equals `nReturned: 107` — the index scan touched exactly the documents it returned, nothing wasted.
- `executionTimeMillis: 0.19` — down from 1.079ms (single-field index) and 3.01ms (no index). A 15× improvement over the baseline with a single well-designed index.

---

### Covered Queries

A **covered query** is one where every field needed — filters, sort, and projection — is present in the index. When the projection excludes `_id` and includes only index fields, the engine can satisfy the query entirely from the index without loading the actual documents, eliminating the `FETCH` stage from the plan.

```javascript
db.orders.find(
  {
    status:     "shipped",
    customerId: "C-1001",
    createdAt:  { $gte: ISODate("2024-01-01") }
  },
  { _id: 0, customerId: 1, status: 1, createdAt: 1 }
).sort({ createdAt: -1 }).explain("executionStats")
```

> **Private Preview:** Covered query support for Find and Project in Azure DocumentDB is currently in private preview. Contact your Microsoft account team to request access.

> **When is this worth it?** Covered queries shine on high-read, high-throughput collections where the projection returns a small subset of a wide document. If the application needs the full document, the `FETCH` is unavoidable.

---

### Hiding an Index

Instead of dropping an index to test whether it is still needed, you can **hide** it. A hidden index is excluded from the query optimizer — reads behave as if it does not exist — but it remains in place and continues to be maintained by all write operations.

```javascript
// Hide the index — optimizer ignores it for reads
db.runCommand({
  collMod: "orders",
  index: { name: "customerId_1_status_1_createdAt_-1", hidden: true }
})

// Unhide the index — optimizer uses it again
db.runCommand({
  collMod: "orders",
  index: { name: "customerId_1_status_1_createdAt_-1", hidden: false }
})
```

> **Why hide instead of drop?** Dropping and recreating a large index can take minutes to hours in production. Hiding is instant and reversible. Use it to confirm that a candidate replacement index handles all relevant queries before permanently removing the old one.

---

### Common Indexing Mistakes

| Mistake | Problem | Fix |
|---|---|---|
| Range field first in compound index | Engine scans a wide key range before applying equality filters | Follow ESR — equality fields first |
| Sort direction mismatch | Optimizer cannot use the index for ordering; in-memory `SORT` added | Match index direction to sort direction (or use the complete reverse) |
| Index on low-cardinality field only | Index eliminates fewer documents than expected | Move low-cardinality fields later in the compound index |
| Over-indexing | Every index adds write latency and storage cost | Create indexes for confirmed query patterns; validate with `explain()` before shipping |

---

### Redundant and Duplicate Indexes

Two common situations waste storage and write performance without providing any query benefit:

**Prefix redundancy** — A compound index `{ a: 1, b: 1, c: 1 }` already serves any query that filters only on `a`, or on `a` and `b`. A separate single-field index `{ a: 1 }` is redundant and can be dropped.

```javascript
// { customerId: 1, status: 1, createdAt: -1 } already covers this query
db.orders.find({ customerId: "C-1001" })

// So this separate index is redundant
db.orders.createIndex({ customerId: 1 })  // unnecessary
```

**Near-duplicate compound indexes** — Two indexes that differ only in one trailing field, or in field order when the queries are identical, both add write overhead for no additional read benefit.

```javascript
// These two indexes serve nearly identical queries; keep the more complete one
db.orders.createIndex({ status: 1, createdAt: -1 })
db.orders.createIndex({ status: 1, createdAt: -1, customerId: 1 })  // superset — drop the first
```

Audit indexes periodically with `db.collection.getIndexes()` and use `explain()` to confirm each index is actually used by a real query pattern before keeping it.

---

### Index Management

```javascript
// List all indexes on the collection
db.orders.getIndexes()

// Drop a specific index by name
db.orders.dropIndex("status_1")

// Drop all non-_id indexes
db.orders.dropIndexes()
```

---

## Success Check

Before the next module, confirm:

- [ ] You can describe when to embed versus when to reference, and give an example of each.
- [ ] You can explain the Extended Reference, Bucket, and Polymorphic patterns and when to use each.
- [ ] You know when sharding becomes relevant and can describe the two distribution modes.
- [ ] You can enable Diagnostic Logs and write a KQL query to find slow operations.
- [ ] You can run `explain("executionStats")` on a `find()` and on an `aggregate()`.
- [ ] You can identify `COLLSCAN`, `IXSCAN`, `FETCH`, and `SORT` stages in an explain output.
- [ ] You understand `totalDocsRemovedByRuntimeFilter` and what it signals.
- [ ] You understand the efficiency ratio: `nReturned / totalDocsExamined`.
- [ ] You can apply the ESR rule to design a compound index.
- [ ] You know how to hide an index and why it is preferable to dropping it for testing.
- [ ] You completed the [Hands-On Lab](lab.md).

## Additional Resources

- [Indexes on Azure DocumentDB](https://learn.microsoft.com/en-us/azure/documentdb/indexing)
- [Read query explain output in Azure DocumentDB](https://learn.microsoft.com/en-us/azure/documentdb/how-to-read-explain-output)
- [Query Performance Tuning Guide — Azure DocumentDB Blog](https://devblogs.microsoft.com/documentdb/query-performance-tuning-guide/)
- [Monitor Azure DocumentDB with Diagnostic Logs](https://learn.microsoft.com/azure/documentdb/how-to-monitor-diagnostics-logs)
