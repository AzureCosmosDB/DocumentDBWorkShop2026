---
title: DocumentDB Introduction and Environment Access
description: Review Azure DocumentDB concepts and connect to the predeployed workshop environment
---

This module introduces Azure DocumentDB and connects the tools on your provided
workshop VM to the predeployed resources used throughout the workshop.

## Module materials

1. Review the [Azure DocumentDB introduction](DocumentDB_Intro.pdf).
2. Confirm the prerequisites below.
3. Complete [Access the workshop environment](cluster-setup.md).

## What participants need

* A computer with internet access and a supported web browser
* The workshop VM access details provided by the instructor
* The Microsoft Entra user principal and password provided by the instructor

The workshop VM provides Visual Studio Code, Azure CLI, the required extensions,
and all language runtimes. The assigned Azure resource group provides the Azure
DocumentDB cluster, Azure OpenAI resource, embedding deployment, network
configuration, and Microsoft Entra role assignments. The supplied user
principal has the administrator access required for the labs. Participants do
not install software or create Azure resources.

## Before the session

Connect to the workshop VM through your browser. Confirm that you can open the
preconfigured VS Code workspace and run:

```powershell
az --version
```

You will use `az login` during environment access. No database password, Azure
OpenAI API key, or DocumentDB connection string is required by the workshop
samples.

## Environment access

Follow [Access the workshop environment](cluster-setup.md) before starting the
remaining modules. The guide describes the resources you should expect, runs
the shared `Set-LabEnvironment.ps1` script, and shows the expected output.

## Common terms

* Database: a logical container for collections
* Collection: a group of documents, similar to a table in a relational system
* Document: a JSON-like record stored as BSON
* Index: a data structure that helps queries avoid scanning every document
* Aggregation pipeline: a sequence of stages used to transform or summarize data

## Success check

Before continuing to Module 2, confirm:

* You are signed in through Azure CLI with the workshop account
* The correct workshop subscription is active
* The shared environment script completes successfully
* The three workshop environment variables are displayed
* You restarted the VS Code terminal and notebook kernel after setup