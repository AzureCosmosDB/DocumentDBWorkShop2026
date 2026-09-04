# Module 2 — Hands-On Lab: Data Modeling and Performance

[Back to Module 2: Data Modeling and Performance](README.md)

Complete these exercises in order. Each exercise builds on the previous one. Run all commands using the DocumentDB for VS Code extension or `mongosh` connected to your cluster.

Before starting, verify the dataset is loaded:

```javascript
use docdbworkshop
db.orders.countDocuments()    // Expected: 10000
db.customers.countDocuments() // Expected: 10
```

If the counts are wrong, return to the [Dataset Setup](README.md#dataset-setup) section.

---

## Exercise 1 — Modeling: Embed vs Reference in Practice

In this exercise you will insert documents using two different modeling approaches, then query them and decide which fits the scenario.

### Scenario

A customer places an order for two books. Each book has a title, author, and price. You need to store the order and its line items.

### 1a — Approach A: Embedded Items

Insert the order with items embedded in an array:

```javascript
db.demo.insertOne({
  _id: "DEMO-001",
  customerId: "C-1001",
  status: "shipped",
  total: 79.90,
  createdAt: new Date("2024-11-01"),
  items: [
    { sku: "BOOK-101", title: "Designing Data-Intensive Applications", qty: 1, unitPrice: 49.95, subtotal: 49.95 },
    { sku: "BOOK-202", title: "MongoDB: The Definitive Guide",         qty: 1, unitPrice: 29.95, subtotal: 29.95 }
  ]
})
```

Retrieve the complete order in a single query:

```javascript
db.demo.findOne({ _id: "DEMO-001" })
```

Retrieve only the item titles and subtotals:

```javascript
db.demo.findOne({ _id: "DEMO-001" }, { "items.title": 1, "items.subtotal": 1, _id: 0 })
```

### 1b — Approach B: Polymorphic (Header + Line Items)

Insert the same order split into three documents in the same `demo` collection:

```javascript
db.demo.insertMany([
  {
    _id:       "DEMO-002",
    orderId:   "DEMO-002",
    type:      "order",
    customerId: "C-1001",
    status:    "shipped",
    total:     79.90,
    createdAt: new Date("2024-11-01")
  },
  {
    _id:      "DEMO-002-1",
    type:     "line_item",
    orderId:  "DEMO-002",
    sku:      "BOOK-101",
    title:    "Designing Data-Intensive Applications",
    qty:      1,
    unitPrice: 49.95,
    subtotal:  49.95
  },
  {
    _id:      "DEMO-002-2",
    type:     "line_item",
    orderId:  "DEMO-002",
    sku:      "BOOK-202",
    title:    "MongoDB: The Definitive Guide",
    qty:      1,
    unitPrice: 29.95,
    subtotal:  29.95
  }
])
```

Retrieve everything for this order in one query:

```javascript
db.demo.find({ orderId: "DEMO-002" }).sort({ type: 1 })
```

Retrieve only line items:

```javascript
db.demo.find({ orderId: "DEMO-002", type: "line_item" }, { title: 1, subtotal: 1, _id: 0 })
```

### 1c — Compare

```javascript
db.demo.countDocuments({ _id: "DEMO-001" })       // Approach A: how many documents?
db.demo.countDocuments({ orderId: "DEMO-002" })    // Approach B: how many documents?
```

🔍 **Exercise question:** This order has 2 items. If the catalog had orders with 500+ line items, which approach would you choose? What happens to Approach A when the items array grows very large?

Clean up:

```javascript
db.demo.drop()
```

---

## Exercise 2 — Explore the Dataset

Get familiar with the data before optimizing it. Drop all non-`_id` indexes first:

```javascript
db.orders.dropIndexes()
```

Count orders by status:

```javascript
db.orders.aggregate([
  { $group: { _id: "$status", count: { $sum: 1 } } },
  { $sort: { count: -1 } }
])
```

Count orders and total revenue for customer `C-1006`:

```javascript
db.orders.aggregate([
  { $match: { customerId: "C-1006" } },
  { $group: { _id: "$status", count: { $sum: 1 }, revenue: { $sum: "$total" } } }
])
```

Write down the approximate count of `"delivered"` orders. You will use it to evaluate index selectivity in the next exercise.

---

## Exercise 3 — Baseline: A New Query

A finance report needs all high-value delivered orders sorted by total descending. Run explain on this query **before** creating any index:

```javascript
db.orders.find(
  { status: "delivered", total: { $gte: 500 } }
).sort({ total: -1 }).explain("executionStats")
```

Record the values:

| Metric | Your value |
|---|---|
| Top-level `winningPlan.stage` | |
| `nReturned` | |
| `totalDocsExamined` (FETCH stage) | |
| `totalDocsRemovedByRuntimeFilter` | |
| `totalKeysExamined` | |
| `executionTimeMillis` | |
| `SORT` stage present? | Yes / No |

🔍 **Exercise question:** Based on the status counts you captured in Exercise 2, roughly what percentage of the 10,000 documents are "delivered"? Does `totalDocsRemovedByRuntimeFilter` match that expectation?

---

## Exercise 4 — Design the Index Yourself

Apply the ESR rule to the query from Exercise 3.

Identify each field's role:

| Field | Role (`$eq`, `$gte`/`$lte`, sort) | ESR position | Index direction |
|---|---|---|---|
| `status` | | | |
| `total` | | | |

> **Hint:** `total` appears in both a range filter (`$gte`) and a sort. When the same field is both sort and range, it occupies the sort position (S).

Create the index you designed:

```javascript
db.orders.createIndex({ /* your index here */ })
```

Re-run the explain from Exercise 3 and complete the right column:

| Metric | Baseline | Your index |
|---|---|---|
| Top-level stage | | |
| `nReturned` | | |
| `totalDocsExamined` | | |
| `totalDocsRemovedByRuntimeFilter` | | |
| `totalKeysExamined` | | |
| `executionTimeMillis` | | |
| `SORT` stage present? | | |

🔍 **Exercise question:** Is `totalDocsRemovedByRuntimeFilter` zero? If not, which filter is still being applied at runtime and why?

---

## Exercise 5 — Sort Field ≠ Range Field

The previous query was simple because `total` was both the range and the sort. Now consider a harder case where the sort field and the range field are different:

```javascript
db.orders.find(
  {
    customerId: "C-1006",
    status:     "shipped",
    total:      { $gte: 100 }
  }
).sort({ createdAt: -1 }).explain("executionStats")
```

Drop any existing indexes first:

```javascript
db.orders.dropIndexes()
```

Run the baseline explain and record the red flags.

Now complete the ESR table:

| Field | Role | ESR position | Direction |
|---|---|---|---|
| `customerId` | | | |
| `status` | | | |
| `createdAt` | | | |
| `total` | | | |

> **The challenge:** `createdAt` is the sort field (position S). `total` is a range filter (position R). Where does `total` go in the index relative to `createdAt`? Does it need to be in the index at all for the query to be efficient?

Create the index and confirm with explain that:
- No `SORT` stage is present.
- `totalDocsRemovedByRuntimeFilter` reflects only the `total` filter (the other two fields should be in `indexFilterSet`).

---

## Exercise 6 — Test Without Dropping: Hide the Index

You suspect the index from Exercise 5 can be improved. Before dropping it, hide it and test a new candidate:

```javascript
// Step 1: hide the existing index so the optimizer ignores it
db.runCommand({
  collMod: "orders",
  index: { name: "<name of your Exercise 5 index>", hidden: true }
})
```

Verify the optimizer no longer uses it:

```javascript
db.orders.find(
  { customerId: "C-1006", status: "shipped", total: { $gte: 100 } }
).sort({ createdAt: -1 }).explain("executionStats")
```

Now create a new compound index that **also includes `total`** at the end (after the sort field):

```javascript
db.orders.createIndex({ customerId: 1, status: 1, createdAt: -1, total: 1 })
```

Re-run explain. Compare `totalDocsRemovedByRuntimeFilter` between the hidden index plan and the new index plan.

If the new index is better, drop the hidden one. If it is not, unhide it and drop the new one.

```javascript
// Unhide command if needed
db.runCommand({
  collMod: "orders",
  index: { name: "<name>", hidden: false }
})
```

🔍 **Exercise question:** Did adding `total` to the index eliminate the remaining `totalDocsRemovedByRuntimeFilter`? Was the improvement worth the extra field in the index?

---

## Exercise 7 — Aggregation: Find the Problem and Fix It

An analytics pipeline calculates average order value per customer for a recent period:

```javascript
db.orders.dropIndexes()

db.orders.explain("executionStats").aggregate([
  { $match: { createdAt: { $gte: ISODate("2024-07-01") }, status: { $ne: "cancelled" } } },
  { $group: { _id: "$customerId", avgOrder: { $avg: "$total" }, orderCount: { $sum: 1 } } },
  { $sort: { avgOrder: -1 } },
  { $limit: 5 }
])
```

From the `$cursor` stage of the explain output:

| Metric | Value |
|---|---|
| `stage` | |
| `totalDocsExamined` | |
| `totalDocsRemovedByRuntimeFilter` | |
| `nReturned` | |

🔍 **Exercise questions before creating an index:**

1. `status: { $ne: "cancelled" }` is a negative filter. Does it make a good index key? Why or why not?
2. `createdAt: { $gte: ISODate("2024-07-01") }` covers approximately the last 6 months of the dataset. About how many documents would an index on `createdAt` return to the `$group` stage?

Create the index you think will help most, re-run the explain, and record how `totalDocsExamined` in the `$cursor` stage changed.

---

## Exercise 8 — Final Challenge: Optimizing an Aggregation Pipeline

> **Scenario:** A leaderboard endpoint runs every 30 seconds to rank the top 5 customers by revenue for shipped orders placed in 2024. The ops team flagged it as one of the slowest queries in Diagnostic Logs.

### 8a — Run the baseline

Drop all indexes and run the explain:

```javascript
db.orders.dropIndexes()

db.orders.explain("executionStats").aggregate([
  { $match: { status: "shipped", createdAt: { $gte: ISODate("2024-01-01") } } },
  { $group: { _id: "$customerId", totalRevenue: { $sum: "$total" }, orderCount: { $sum: 1 } } },
  { $sort:  { totalRevenue: -1 } },
  { $limit: 5 }
])
```

Record the `$cursor` stage:

| Metric | Baseline |
|---|---|
| `stage` | |
| `totalDocsExamined` | |
| `totalDocsRemovedByRuntimeFilter` | |
| `nReturned` (docs fed to `$group`) | |

### 8b — Design the index

Think about the pipeline as a whole — not just the `$match`. Which stages can benefit from an index, and how should the index be structured to help the most operations at once?

Design and create the index:

```javascript
db.orders.createIndex({ /* your index here */ })
```

Re-run the same explain and record the comparison:

| Metric | Baseline | With index |
|---|---|---|
| `$cursor` stage | | |
| `totalDocsExamined` | | |
| `totalDocsRemovedByRuntimeFilter` | | |
| `nReturned` (docs fed to `$group`) | | |
| `$group` `aggStrategy` | | |

🔍 **Validation questions:**

1. Is `totalDocsRemovedByRuntimeFilter` zero? If not, which filter is still applied at runtime?
2. Did `aggStrategy` change? What does that tell you about how the `$group` is executing?
3. Is the `$sort` on `totalRevenue` still present? Is that a problem?

### 8c — Understand the limits

🔍 **Exercise questions:**

1. The `$sort` on `totalRevenue` is still present in every explain output. Why can no index ever eliminate it?
2. Looking at your validation from 8b: if `totalDocsRemovedByRuntimeFilter` is still non-zero, which field is the culprit and what would you need to add to the index to eliminate it?
3. The `$group` stage processes fewer documents than the baseline — but is it zero-cost? What determines how much work it still has to do?

---

## Lab Success Check

- [ ] You inserted and queried documents using both the Embedded and Polymorphic patterns and can explain the trade-off.
- [ ] You ran `explain("executionStats")` on an unfamiliar query and recorded the key red flags.
- [ ] You applied the ESR rule independently in Exercise 4 without hints.
- [ ] You handled a query where the sort field and the range field are different (Exercise 5).
- [ ] You used index hiding to compare two candidate indexes without downtime (Exercise 6).
- [ ] You ran `explain()` on an aggregation pipeline and identified which stage benefits from an index.
- [ ] You completed the final challenge: reduced `totalDocsExamined` in the `$cursor` stage and explained why `$sort` on a computed field cannot use an index.
