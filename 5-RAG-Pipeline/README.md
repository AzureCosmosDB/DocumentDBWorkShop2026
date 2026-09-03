---
title: RAG Pipeline with Azure DocumentDB
description: Run keyless Python, Node.js, and C# RAG samples with Azure DocumentDB and Azure OpenAI
---

**Duration:** 60 minutes

Build a retrieval-augmented generation (RAG) workflow that embeds source
chunks, retrieves relevant context from Azure DocumentDB, and constructs a
grounded prompt.

## Learning goals

* Store source chunks and vector embeddings together
* Retrieve context with vector search
* Add keyword candidates when full-text search is available
* Fuse ranked lists with RRF
* Build a prompt that limits the model to retrieved context
* Authenticate without database passwords or API keys

## Prerequisites

* Complete the Module 1 environment-access lab
* Complete the Module 4 search lab
* Use the selected Python, Node.js, or .NET environment provided on the
  workshop VM
* Restart the terminal or notebook kernel after environment setup

## Lab step 1: Configure the environment

From the workspace root, sign in and run the shared setup script:

```powershell
az login
./1-DocumentDB-Introduction-and-Cluster-Setup/Set-LabEnvironment.ps1
```

Restart the notebook kernel or terminal after the script persists the selected
resource settings.

## Lab step 2: Choose a language track

### Python

Open [the Python RAG notebook](before/python/5_RAG_Pipeline_DocumentDB.ipynb),
select a Python kernel, and run the cells in order. Complete each `STUDENT
EXERCISE` before comparing your work with the
[completed Python reference](after/python/5_RAG_Pipeline_DocumentDB.ipynb).

### Node.js

```powershell
Set-Location ./5-RAG-Pipeline/before/nodejs
npm start
```

### C#

```powershell
dotnet run --project ./5-RAG-Pipeline/before/csharp/DocumentDbRag.csproj
```

All tracks use `AzureCliCredential` for Azure DocumentDB and Azure OpenAI.

## Lab step 3: Create the retrieval store

Observe the sample as it embeds four source chunks with
`text-embedding-3-small` and writes them to
`docdbworkshop.rag_chunks`. Confirm that each document retains its title,
source identifier, URL, tags, chunk text, and embedding.

Create or verify the DiskANN vector index. The samples also attempt to create a
BM25 full-text index. Full-text search is a gated preview; the Node.js and C#
samples report code `115` and continue with vector-only retrieval when it is
not enabled.

## Lab step 4: Retrieve and rank context

Embed the question `How does DocumentDB retrieve context for RAG?` and retrieve
the three nearest chunks. Inspect the titles, chunk text, source URLs, and
search scores.

When full-text search is available, run the BM25 query and use RRF to combine
the keyword and vector lists. If it is unavailable, use the vector results as
the retrieved context.

## Lab step 5: Build the grounded prompt

Format the retrieved chunks inside `<context>` tags. The prompt must instruct
the model to answer only from that context and to say that it does not know
when the answer is absent.

Review the printed prompt and verify that every included claim can be traced to
a retrieved chunk and source URL.

## Lab success check

* [ ] Your selected language authenticates with `AzureCliCredential`
* [ ] Four chunks are stored with embeddings and source metadata
* [ ] The embedding dimension is reported after loading the chunks
* [ ] The DiskANN vector index is created successfully
* [ ] Vector retrieval returns relevant context for the sample question
* [ ] You recorded whether full-text search and hybrid retrieval are available
* [ ] The final prompt contains numbered sources inside `<context>` tags
* [ ] The prompt instructs the model not to answer beyond retrieved context
