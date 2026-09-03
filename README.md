---
title: Azure DocumentDB Workshop
description: Start-to-finish guide for the Azure DocumentDB data modeling, migration, search, and RAG labs
---

Welcome to the Azure DocumentDB workshop. Start on this page, complete the
modules in order, and use the navigation tables to return to the correct lab.
The workshop supports Python, C#, and Node.js application developers.

You complete the labs on a provided workshop virtual machine (VM) through your
web browser. The VM contains the required tools, extensions, language runtimes,
and lab files. The required Azure resources are already deployed in your
assigned resource group, so you do not need to install software or deploy
infrastructure.

> [!IMPORTANT]
> Use the numbered folders in this workspace as the current lab. The ZIP file
> at the repository root is a historical delivery artifact, not a second copy
> to extract or follow during the workshop.

## Workshop outcomes

By the end of the workshop, you will be able to:

* Access a predeployed Azure DocumentDB environment with Microsoft Entra ID
* Choose document models that match application access patterns
* Diagnose query plans and design indexes with the ESR rule
* Assess and migrate MongoDB data to Azure DocumentDB
* Build vector, full-text, and hybrid retrieval workflows
* Construct a grounded retrieval-augmented generation (RAG) prompt
* Use Azure CLI credentials instead of database passwords and API keys

## Suggested agenda

The labs can run in one full day or in two sessions. Breaks are not included in
the times below.

| Session | Module | Topic | Duration |
|---|---|---|---|
| Session 1 | 1 | Introduction and environment access | 30 minutes |
| Session 1 | 2 | Data modeling and performance | 90 minutes |
| Session 1 | 3 | Data migration | 120 minutes |
| Session 2 | 4 | Vector, full-text, and hybrid search | 75 minutes |
| Session 2 | 5 | RAG pipeline | 60 minutes |

Session 1 establishes the data platform and migration foundation. Session 2
uses the migrated and modeled data in language-specific search and RAG
applications.

## Start here

### What you need

You only need:

* A computer with internet access
* A supported web browser
* The workshop VM access details provided by the instructor
* The Microsoft Entra user principal and password provided by the instructor

The supplied user principal has the administrator access required for the
workshop. Use it to sign in to the Azure portal and Azure CLI. Do not use a
personal or corporate Azure account unless the instructor directs you to do
so.

### What is already provided

The workshop environment includes:

* A browser-accessible VM with the repository and required lab files
* Azure CLI, PowerShell, Python, Node.js, and the .NET SDK
* VS Code, Jupyter, and the required DocumentDB extensions
* An Azure resource group containing the required Azure DocumentDB and Azure
  OpenAI resources
* Permissions for the supplied user principal to complete the labs

### Connect to the lab

Complete these steps once before opening a module:

1. Use the instructor-provided link to connect to the workshop VM in your web
   browser.
2. Open the preconfigured `docdbworkshop` workspace in VS Code on the VM.
3. Open the Azure portal in the VM browser and sign in with the supplied
   Microsoft Entra user principal.
4. Confirm that you can view the assigned Azure resource group and its
   predeployed resources.
5. Open a PowerShell terminal at the repository root.
6. Sign in to Azure CLI with the same supplied user principal and configure the
   workshop environment.

```powershell
az login
./1-DocumentDB-Introduction-and-Cluster-Setup/Set-LabEnvironment.ps1
```

Follow the browser prompt from `az login` and use the supplied workshop
credentials. If interactive browser sign-in is unavailable, run
`az login --use-device-code`. When the script completes, restart the VS Code
terminal and any notebook kernel so new processes receive the persisted
environment variables.

The script should report:

```text
DOCUMENTDB_CLUSTER_NAME=<assigned-cluster>
AZURE_OPENAI_ENDPOINT=<assigned-endpoint>
AZURE_OPENAI_EMBEDDING_DEPLOYMENT=text-embedding-3-small
```

These values are resource identifiers, not passwords or API keys.

## Choose a language track

Modules 2, 4, and 5 provide Python, C#, and Node.js implementations. Choose one
language and use it consistently. Module 3 uses a shared migration workflow for
all participants.

### Python

Python 3.10 or later, the VS Code Python extension, Jupyter, and the required
packages are provided on the workshop VM. Confirm the interpreter:

```powershell
python --version
```

Use these notebooks:

* [Module 2 Python modeling notebook](2-Data-Modeling-and-Performance/python/2_Data_Modeling_Performance.ipynb)
* [Module 4 Python search notebook](4-Search/before/python/4_Search_DocumentDB.ipynb)
* [Module 5 Python RAG notebook](5-RAG-Pipeline/before/python/5_RAG_Pipeline_DocumentDB.ipynb)

Select the intended Python kernel in each notebook and run cells in order. The
required Python packages are already available on the workshop VM. Complete
each `STUDENT EXERCISE` before continuing.

### Node.js

Node.js 20 or later and the required packages are provided on the workshop VM.
Confirm the toolchain:

```powershell
node --version
npm --version
```

Run Module 2 from the repository root:

```powershell
npm --prefix ./2-Data-Modeling-and-Performance/nodejs start
```

Run Module 4 from the repository root:

```powershell
npm --prefix ./4-Search/before/nodejs start
```

Run Module 5:

```powershell
npm --prefix ./5-RAG-Pipeline/before/nodejs start
```

### C#

The .NET 10 SDK and required packages are provided on the workshop VM. Confirm
that the SDK is available:

```powershell
dotnet --list-sdks
```

Run Module 2 from the repository root:

```powershell
dotnet run --project ./2-Data-Modeling-and-Performance/csharp/DocumentDbModeling.csproj
```

Run Module 4 from the repository root:

```powershell
dotnet run --project ./4-Search/before/csharp/DocumentDbSearch.csproj
```

Run Module 5:

```powershell
dotnet run --project ./5-RAG-Pipeline/before/csharp/DocumentDbRag.csproj
```

All three tracks use `AzureCliCredential` for Azure DocumentDB and Azure
OpenAI. Do not add connection strings, database passwords, or API keys to the
sample files.

## Workshop navigation

Complete the modules in order. Use each module's lab success check before
moving to the next row.

| Module | Start page | What you will do | Completion evidence |
|---|---|---|---|
| 1 | [Introduction and environment access](1-DocumentDB-Introduction-and-Cluster-Setup/README.md) | Sign in, inspect provided resources, and configure environment variables | The setup script prints all three environment variables |
| 2 | [Data modeling and performance](2-Data-Modeling-and-Performance/README.md) | Compare document models, read explain plans, and design indexes | The optimized plan examines fewer documents |
| 3 | [Data migration](3-Data-Migration/README.md) | Assess, migrate, and validate a provided MongoDB source | Source and target counts are reconciled |
| 4 | [Search](4-Search/README.md) | Generate embeddings and run vector, keyword, and hybrid retrieval | Vector search returns ranked documents |
| 5 | [RAG pipeline](5-RAG-Pipeline/README.md) | Retrieve source chunks and build a grounded prompt | The prompt contains numbered retrieved sources |

## Module map

### Module 1: Environment access

Start with the [Module 1 overview](1-DocumentDB-Introduction-and-Cluster-Setup/README.md),
then follow [Access the workshop environment](1-DocumentDB-Introduction-and-Cluster-Setup/cluster-setup.md).
The resources are already deployed. Do not create a new cluster.

### Module 2: Data modeling and performance

Open the [Module 2 lab guide](2-Data-Modeling-and-Performance/README.md), choose
Python, C#, or Node.js, and compare the same deterministic dataset and query
plans through that language's driver.

### Module 3: Data migration

Follow the [Module 3 migration lab](3-Data-Migration/README.md). Use the source
connection details provided by the instructor and select the predeployed Azure
DocumentDB target. This module is shared by all language tracks.

### Module 4: Search

Open the [Module 4 lab guide](4-Search/README.md), then follow only your selected
Python, C#, or Node.js track. The lab covers DiskANN vector search and, when the
preview is enabled, BM25, fuzzy, phrase, and hybrid search.

### Module 5: RAG pipeline

Open the [Module 5 lab guide](5-RAG-Pipeline/README.md), then follow the same
language selected for Module 4. The lab stores source chunks, retrieves context,
and produces a grounded prompt.

## Expected preview behavior

DiskANN vector search requires a supported Azure DocumentDB cluster tier.
Full-text search is a gated preview and may not be enabled on the assigned
cluster. This server response is expected when the preview is unavailable:

```text
code: 115
codeName: CommandNotSupported
errmsg: Search index creation is not supported yet
```

This error does not mean that Azure login, Microsoft Entra authentication, or
vector search failed. The Python, C#, and Node.js labs continue with vector-only
retrieval where full-text search is unavailable.

## When you get stuck

### Azure sign-in fails

Run `az login --use-device-code`, then confirm the selected subscription:

```powershell
az account show --output table
```

### Environment variables are missing

Run the shared setup script from the repository root, then restart the terminal
or notebook kernel. Do not run a later notebook cell before its setup cell.

### Python reports an undefined variable

Restart the kernel and run notebook cells from the top. Variables such as
`chunks` are created by the first Python setup cell.

### Node.js cannot find a package

The workshop VM should contain all required packages. Confirm that you opened
the provided workspace on the assigned VM. If a package is missing, report the
VM name and module number to the instructor.

### C# cannot build

Run `dotnet --list-sdks`. If the command does not list the .NET 10 SDK, report
the VM name to the instructor. Participants do not need to install the SDK.

### Search index creation returns code 115

Continue with vector search. The full-text search preview is not enabled for the
assigned cluster.

## Workshop completion check

* [ ] You completed each module's lab success check
* [ ] You used one language consistently in Modules 4 and 5
* [ ] You authenticated with Azure CLI credentials and did not add secrets
* [ ] You captured evidence from query plans, migration validation, and search results
* [ ] You produced a grounded prompt containing retrieved source context
