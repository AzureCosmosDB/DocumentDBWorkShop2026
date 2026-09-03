---
title: Access the Workshop Environment
description: Sign in to Azure and configure the workshop VM for predeployed Azure DocumentDB resources
---

The workshop environment is deployed before the lab begins. You do not need to
create an Azure DocumentDB cluster, configure networking, copy connection
strings, or manage access keys.

## Resources provided for the workshop

Your assigned Azure subscription contains the resources used by the labs:

* An Azure DocumentDB cluster
* An Azure OpenAI or Azure AI Services account
* A `text-embedding-3-small` deployment
* Role assignments for Microsoft Entra ID authentication
* Network access configured for the workshop environment

The search labs expect a cluster tier that supports DiskANN vector indexes.
Full-text search is a gated preview and may not be enabled on every workshop
cluster. The console samples continue with vector-only retrieval when the
server returns `CommandNotSupported` for full-text index creation.

## Sign in and configure your environment

Open PowerShell at the workspace root and sign in with the account assigned to
the workshop:

```powershell
az login
```

If your account has access to multiple subscriptions, confirm the active one:

```powershell
az account show --output table
```

Select the workshop subscription when necessary:

```powershell
az account set --subscription "<workshop-subscription-name-or-id>"
```

Run the shared environment script:

```powershell
./1-DocumentDB-Introduction-and-Cluster-Setup/Set-LabEnvironment.ps1
```

When more than one matching resource is available, the script displays a
numbered list. Select the Azure DocumentDB cluster, Azure OpenAI account, and
embedding deployment assigned to you.

## Expected script output

The script reports the active subscription and prints three values similar to
the following example:

```text
Using Azure subscription: Workshop Subscription
DOCUMENTDB_CLUSTER_NAME=docdb-workshop-01
AZURE_OPENAI_ENDPOINT=https://workshop-openai.openai.azure.com
AZURE_OPENAI_EMBEDDING_DEPLOYMENT=text-embedding-3-small
Variables were persisted for future processes. Restart the notebook kernel or terminal before running a lab.
```

The actual resource names and endpoint depend on your assigned environment.
The script stores these values as user environment variables, so no secrets or
connection strings are written to the repository.

| Environment variable | Used for |
|---|---|
| `DOCUMENTDB_CLUSTER_NAME` | Building the Azure DocumentDB SRV endpoint |
| `AZURE_OPENAI_ENDPOINT` | Sending requests to the assigned Azure OpenAI resource |
| `AZURE_OPENAI_EMBEDDING_DEPLOYMENT` | Selecting the embedding deployment |

Restart the VS Code terminal and any notebook kernel after the script
completes. New processes then receive the persisted values.

## Expected authentication behavior

The Python, Node.js, and C# samples use `AzureCliCredential`. When a sample
starts successfully, expect it to:

1. Reuse the identity from your active Azure CLI session.
2. Request a Microsoft Entra token for Azure DocumentDB.
3. Request a separate Microsoft Entra token for Azure OpenAI.
4. Connect without a database password or API key.

The first database check returns a successful `ping`, and the sample reports
that it connected with Microsoft Entra ID.

## Troubleshooting expected access

If `az login` fails, complete the browser sign-in flow and verify that you used
the workshop account. For environments where the browser cannot open, use:

```powershell
az login --use-device-code
```

If the setup script cannot find the expected resources:

1. Run `az account show` and confirm the workshop subscription is active.
2. Confirm that your workshop role assignments have been applied.
3. Ask the instructor for the assigned resource group or resource names.
4. Run the script again with the provided resource group when needed:

```powershell
./1-DocumentDB-Introduction-and-Cluster-Setup/Set-LabEnvironment.ps1 `
    -ResourceGroupName "<workshop-resource-group>"
```

An error stating that search index creation is not supported means full-text
search is not enabled for that cluster. It does not indicate a failed Azure
login or DocumentDB connection.