#!/usr/bin/env pwsh
# Copyright (c) Microsoft Corporation.
# SPDX-License-Identifier: MIT
#Requires -Version 7.0

<#
.SYNOPSIS
	Configures environment variables for the DocumentDB workshop labs.
.DESCRIPTION
	Uses the active Azure CLI login to discover an Azure DocumentDB cluster,
	Azure OpenAI endpoint, and embedding deployment. Values are set for the
	current process and, by default, future user processes.
.PARAMETER ResourceGroupName
	Limits resource discovery to one resource group.
.PARAMETER DocumentDbClusterName
	Selects a specific Azure DocumentDB cluster.
.PARAMETER AzureOpenAIAccountName
	Selects a specific Azure OpenAI account.
.PARAMETER EmbeddingDeploymentName
	Selects an embedding deployment. Defaults to textembedding3small.
.PARAMETER SessionOnly
	Sets variables only in the current PowerShell process.
.EXAMPLE
	./Set-LabEnvironment.ps1
.EXAMPLE
	./Set-LabEnvironment.ps1 -ResourceGroupName my-workshop-rg
.NOTES
	Run az login before invoking this script. Restart the notebook kernel after
	the script completes so it can read newly persisted user variables.
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory = $false)]
	[ValidateNotNullOrEmpty()]
	[string]$ResourceGroupName,

	[Parameter(Mandatory = $false)]
	[ValidateNotNullOrEmpty()]
	[string]$DocumentDbClusterName,

	[Parameter(Mandatory = $false)]
	[ValidateNotNullOrEmpty()]
	[string]$AzureOpenAIAccountName,

	[Parameter(Mandatory = $false)]
	[ValidateNotNullOrEmpty()]
	[string]$EmbeddingDeploymentName = 'textembedding3small',

	[Parameter(Mandatory = $false)]
	[switch]$SessionOnly
)

$ErrorActionPreference = 'Stop'

#region Functions
function Invoke-AzureCliJson {
	<#
	.SYNOPSIS
		Invokes Azure CLI and parses its JSON response.
	.PARAMETER ArgumentList
		Arguments passed to the Azure CLI executable.
	.OUTPUTS
		System.Object
	#>
	[CmdletBinding()]
	[OutputType([object])]
	param(
		[Parameter(Mandatory = $true)]
		[string[]]$ArgumentList
	)

	$Json = & az @ArgumentList --output json
	if ($LASTEXITCODE -ne 0) {
		throw "Azure CLI command failed: az $($ArgumentList -join ' ')"
	}

	return $Json | ConvertFrom-Json
}

function Select-SingleResource {
	<#
	.SYNOPSIS
		Selects one Azure resource by optional name.
	.PARAMETER Resource
		Candidate Azure resources.
	.PARAMETER Name
		Optional resource name to select.
	.PARAMETER ResourceLabel
		Human-readable resource type used in errors.
	.OUTPUTS
		System.Management.Automation.PSCustomObject
	#>
	[CmdletBinding()]
	[OutputType([pscustomobject])]
	param(
		[Parameter(Mandatory = $true)]
		[AllowEmptyCollection()]
		[object[]]$Resource,

		[Parameter(Mandatory = $false)]
		[string]$Name,

		[Parameter(Mandatory = $true)]
		[ValidateNotNullOrEmpty()]
		[string]$ResourceLabel
	)

	$Matches = if ($Name) {
		@($Resource | Where-Object Name -EQ $Name)
	}
	else {
		@($Resource)
	}

	if ($Matches.Count -eq 0) {
		throw "No $ResourceLabel was found. Check the active subscription or provide a resource name."
	}
	if ($Matches.Count -gt 1) {
		$Matches = @($Matches | Sort-Object Name)
		Write-Host "Select a $ResourceLabel`:" -ForegroundColor Cyan
		for ($Index = 0; $Index -lt $Matches.Count; $Index++) {
			$ResourceGroup = if ($Matches[$Index].ResourceGroup) {
				" [$($Matches[$Index].ResourceGroup)]"
			}
			else {
				''
			}
			Write-Host "  $($Index + 1). $($Matches[$Index].Name)$ResourceGroup"
		}

		$Selection = Read-Host "Enter a number from 1 to $($Matches.Count)"
		$SelectionNumber = 0
		if (-not [int]::TryParse($Selection, [ref]$SelectionNumber) -or
			$SelectionNumber -lt 1 -or
			$SelectionNumber -gt $Matches.Count) {
			throw "Invalid $ResourceLabel selection '$Selection'."
		}

		return $Matches[$SelectionNumber - 1]
	}

	return $Matches[0]
}
#endregion Functions

#region Main Execution
if ($MyInvocation.InvocationName -ne '.') {
	try {
		if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
			throw 'Azure CLI is not installed or is not available on PATH.'
		}

		$Account = Invoke-AzureCliJson -ArgumentList @('account', 'show')
		Write-Host "Using Azure subscription: $($Account.name)" -ForegroundColor Cyan

		$ResourceArguments = @('resource', 'list')
		if ($ResourceGroupName) {
			$ResourceArguments += @('--resource-group', $ResourceGroupName)
		}
		$Resources = @(Invoke-AzureCliJson -ArgumentList $ResourceArguments)

		$DocumentDbResources = @(
			$Resources | Where-Object Type -EQ 'Microsoft.DocumentDB/mongoClusters'
		)
		$DocumentDb = Select-SingleResource `
			-Resource $DocumentDbResources `
			-Name $DocumentDbClusterName `
			-ResourceLabel 'Azure DocumentDB cluster'

		$OpenAiResources = @(
			$Resources | Where-Object {
				$_.Type -eq 'Microsoft.CognitiveServices/accounts' -and
				$_.Kind -in @('OpenAI', 'AIServices')
			}
		)
		$OpenAi = Select-SingleResource `
			-Resource $OpenAiResources `
			-Name $AzureOpenAIAccountName `
			-ResourceLabel 'Azure OpenAI account'

		$OpenAiDetails = Invoke-AzureCliJson -ArgumentList @(
			'cognitiveservices', 'account', 'show',
			'--name', $OpenAi.Name,
			'--resource-group', $OpenAi.ResourceGroup
		)
		$OpenAiEndpoint = $OpenAiDetails.properties.endpoint
		if (-not $OpenAiEndpoint) {
			throw "Azure OpenAI endpoint was not returned for account '$($OpenAi.Name)'."
		}

		$Deployments = @(
			Invoke-AzureCliJson -ArgumentList @(
				'cognitiveservices', 'account', 'deployment', 'list',
				'--name', $OpenAi.Name,
				'--resource-group', $OpenAi.ResourceGroup
			)
		)
		$EmbeddingDeployments = @(
			$Deployments | Where-Object { $_.properties.model.name -like '*embedding*' }
		)
		$EmbeddingDeployment = Select-SingleResource `
			-Resource $EmbeddingDeployments `
			-Name $EmbeddingDeploymentName `
			-ResourceLabel 'Azure OpenAI embedding deployment'

		$DocumentDbConnectionUri = "mongodb+srv://$($DocumentDb.Name).global.mongocluster.cosmos.azure.com/"

		$Variables = [ordered]@{
			DOCUMENTDB_CLUSTER_NAME          = $DocumentDb.Name
			DOCUMENTDB_CONNECTION_URI        = $DocumentDbConnectionUri
			AZURE_OPENAI_ENDPOINT             = $OpenAiEndpoint.TrimEnd('/')
			AZURE_OPENAI_EMBEDDING_DEPLOYMENT = $EmbeddingDeployment.Name
		}

		foreach ($Entry in $Variables.GetEnumerator()) {
			[Environment]::SetEnvironmentVariable($Entry.Key, $Entry.Value, 'Process')
			if (-not $SessionOnly) {
				[Environment]::SetEnvironmentVariable($Entry.Key, $Entry.Value, 'User')
			}
			Write-Host "$($Entry.Key)=$($Entry.Value)" -ForegroundColor Green
		}

		if ($SessionOnly) {
			Write-Host 'Variables are available in this PowerShell session.' -ForegroundColor Yellow
		}
		else {
			Write-Host 'Variables were persisted for future processes. Restart the notebook kernel or terminal before running a lab.' -ForegroundColor Yellow
		}
	}
	catch {
		Write-Error -ErrorAction Continue "Environment setup failed: $($_.Exception.Message)"
		exit 1
	}
}
#endregion Main Execution