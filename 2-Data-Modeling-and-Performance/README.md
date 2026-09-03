---
title: Data Modeling and Performance
description: Compare Azure DocumentDB models and query plans with Python, C#, or Node.js
---

**Duration:** 90 minutes

Use a programmatic sample to create deterministic workshop data, compare two
document-modeling patterns, inspect query plans, and measure the effect of ESR
indexes. Choose one language and use it for the entire module.

## Learning goals

* Decide when to embed related data and when to store separate documents
* Read `executionStats` from a query plan
* Compare `totalDocsExamined` and `totalKeysExamined`
* Apply the equality, sort, range (ESR) index rule
* Explain why an index cannot sort by a value computed during aggregation

## Prerequisites

* Complete the Module 1 environment-access lab
* Use the supplied workshop VM and Microsoft Entra user principal
* Run the shared environment script and restart the terminal or notebook kernel

The VM contains all required runtimes, extensions, packages, and project files.
Do not install software during the lab.

## What every track does

Each implementation uses `AzureCliCredential` and the
`DOCUMENTDB_CLUSTER_NAME` environment variable. The tracks perform the same
operations with the same deterministic data:

1. Connect to the `docdbworkshop` database with MongoDB OIDC.
2. Reset the `customers`, `orders`, and `demo` collections.
3. Insert 10 customers and 10,000 deterministic orders.
4. Compare one embedded order with a three-document polymorphic order.
5. Display the order-status distribution.
6. Compare a finance query before and after `{ status: 1, total: -1 }`.
7. Compare a distinct sort/range query before and after
   `{ customerId: 1, status: 1, createdAt: -1, total: 1 }`.
8. Return the top five customers in the shipped-order leaderboard.

> [!IMPORTANT]
> Run only one language track. Every track resets the same Module 2 collections,
> so running multiple tracks repeats the lab rather than adding new data.

## Choose a language

### Python notebook

Open the [Python notebook](python/2_Data_Modeling_Performance.ipynb), select the
provided Python kernel, and run all cells in order.

The notebook pauses between stages so you can inspect each result and answer
the reflection questions before continuing.

### C# console application

From the workspace root, run:

```powershell
dotnet run --project ./2-Data-Modeling-and-Performance/csharp/DocumentDbModeling.csproj
```

### Node.js console application

From the workspace root, run:

```powershell
npm --prefix ./2-Data-Modeling-and-Performance/nodejs start
```

## Interpret the results

For both query comparisons, record these values from the baseline and indexed
output:

| Metric | Baseline | ESR index |
|---|---|---|
| `nReturned` | | |
| `totalDocsExamined` | | |
| `totalKeysExamined` | | |
| `executionTimeMillis` | | |

Discuss these questions:

1. Why does the embedded model need one document while the polymorphic model
   needs three?
2. Which index produces the largest reduction in documents examined?
3. Why does `createdAt` appear before `total` in the second compound index?
4. Why can no stored index remove the leaderboard sort on `totalRevenue`?

## Lab success check

* [ ] The selected track connects with Microsoft Entra ID
* [ ] The output reports 10 customers and 10,000 orders
* [ ] The model comparison reports one embedded and three polymorphic documents
* [ ] Both baseline and indexed explain summaries are displayed
* [ ] The indexed plans examine fewer documents than their baselines
* [ ] The leaderboard returns up to five customers
* [ ] You can explain the modeling and index tradeoffs

Continue to [Module 3: Data Migration](../3-Data-Migration/README.md).