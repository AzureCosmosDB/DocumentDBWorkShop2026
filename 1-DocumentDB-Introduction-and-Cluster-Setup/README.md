# Module 1: DocumentDB Introduction and Cluster Setup

This module introduces Azure DocumentDB and prepares the target cluster used throughout the workshop.

## Module Materials

1. Review the [Azure DocumentDB Introduction](DocumentDB_Intro.pdf).
2. Confirm the prerequisites below.
3. Complete [Create or Verify the Target Cluster](cluster-setup.md).

## What Participants Need

- An Azure subscription with permission to create resources, or access to a pre-provisioned lab environment.
- [Visual Studio Code](https://code.visualstudio.com/download).
- [DocumentDB for VS Code extension](https://marketplace.visualstudio.com/items?itemName=ms-azuretools.vscode-documentdb).
- [Azure DocumentDB Migration extension](https://marketplace.visualstudio.com/items?itemName=ms-azurecosmosdbtools.vscode-mongo-migration).
- `mongosh` installed and available in a terminal.
- Optional: `mongoimport` if attendees will load sample data themselves.
- Optional for the MCP demo: Node.js 18+ and GitHub Copilot Chat access.

## Before the Session

Ask participants to verify the following:

- They can sign in to the Azure portal.
- They can open VS Code.
- They can run `mongosh --version`.
- They know where to find the workshop files in this workspace.

If `mongosh` is missing on Windows, install it before the workshop:

```powershell
winget install MongoDB.Shell
```

Then close and reopen the terminal and run:

```powershell
mongosh --version
```

If participants will create their own clusters, also ask them to verify:

- They have enough Azure quota in the selected region for the chosen workshop tier.
- They can create resources in the target subscription and resource group.
- They understand expected workshop cost and cleanup instructions.

## Target Cluster Setup

Before continuing, complete the [Create or Verify the Target Cluster](cluster-setup.md) guide. It covers cluster creation, networking, connection strings, and health checks.

## Common Terms

- Database: a logical container for collections.
- Collection: a group of documents, similar to a table in a relational system.
- Document: a JSON-like record stored as BSON.
- Index: a data structure that helps queries avoid scanning every document.
- Aggregation pipeline: a sequence of stages used to transform or summarize data.

## MongoDB Shell Primer

If this is your first time using MongoDB, practice these commands before the lab.

### Connect to a Cluster

```bash
mongosh "<your-connection-string>"
```

### Basic Navigation

```javascript
show dbs
use cosmicworks
show collections
```

### Insert a Document

```javascript
db.practice.insertOne({
  name: "Ada",
  role: "engineer",
  level: 200,
  active: true
})
```

### Read Documents

```javascript
db.practice.find()

db.practice.find(
  { level: { $gte: 100 } },
  { _id: 0, name: 1, role: 1 }
)
```

### Update a Document

```javascript
db.practice.updateOne(
  { name: "Ada" },
  { $set: { level: 300 } }
)
```

### First Aggregation Example

```javascript
db.practice.aggregate([
  { $group: { _id: "$role", count: { $sum: 1 } } }
])
```

### Delete a Document

```javascript
db.practice.deleteOne({ name: "Ada" })
```

## Success Check

Before the live session, confirm:

- [ ] You can open VS Code and the Azure portal.
- [ ] `mongosh` works in a terminal.
- [ ] You understand how to run `use`, `find`, `insertOne`, `updateOne`, and `aggregate`.