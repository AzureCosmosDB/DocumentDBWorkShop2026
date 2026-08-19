# Module 3: Data Migration

**Duration:** 2 hours

In this lab, you will connect to the target cluster created or verified in Module 1 and migrate data from a MongoDB cluster to Azure DocumentDB.

## Learning Goals

- Connect to a source MongoDB cluster and destination Azure DocumentDB from VS Code.
- Run a pre-migration assessment.
- Perform migration.
- Validate that data and application behavior are preserved.

## Prerequisites

- An Azure DocumentDB target cluster and connection string from the [Module 1 cluster setup guide](../1-DocumentDB-Introduction-and-Cluster-Setup/cluster-setup.md).
- Access to the MongoDB source connection string from the instructor.
- DocumentDB for VS Code extension installed.
- Azure DocumentDB Migration extension installed.

## Part A: Connect to the MongoDB Source in VS Code

1. Open VS Code.
2. Open **Extensions**.
3. Search and install these two extensions:
   - **DocumentDB for VS Code**
   - **Azure DocumentDB Migration**

![VS Code Marketplace showing required DocumentDB extensions](assets/documentdb-extensions-search.png)

4. Open the DocumentDB extension view.
5. Add a MongoDB connection using the source connection string provided by the instructor.
6. Confirm that the source cluster appears in the connections pane.
7. Add another connection using your target connection string from the [Module 1 cluster setup guide](../1-DocumentDB-Introduction-and-Cluster-Setup/cluster-setup.md).
8. Confirm that both source MongoDB and destination Azure DocumentDB connections appear in the pane.

## Part B: Run Pre-Migration Assessment

This is the most important step before any migration.

1. Right-click the MongoDB source connection.
2. Select the data migration option.
3. If prompted, install the Azure DocumentDB Migration extension.
4. If you see a card named **Migration to Azure DocumentDB**, click it.
5. Then choose **Pre-Migration Assessment for Azure DocumentDB**.
6. Validate the source connection.
7. Enter an assessment name.
8. Start the assessment.
9. When it completes, review the report.

### Review the Report For

- Compatibility findings
- Unsupported or partially supported features
- Index or query considerations
- Migration recommendations

### Discussion Prompt

Before proceeding, "What would block a clean migration if this were production?"

## Part C: Perform an Offline Migration

Choose one of the following options.

### Option 1: VS Code Migration Wizard (Recommended)

1. Right-click the MongoDB source again.
2. Select **Migrate to Azure DocumentDB**.
3. Create a migration job.
4. Select **Offline** migration mode.
5. Choose public networking unless the instructor says otherwise.
6. Select the Azure subscription, resource group, and target cluster.
7. Create or reuse an Azure DMS instance.
8. Update firewall rules if prompted.
9. Select the database and collection set to migrate.
10. Start the migration.

### Option 2: CLI Export/Import (`mongoexport` + `mongoimport`)

Use this if the migration wizard is unavailable.

Install prerequisite (Windows) if `mongoimport` is missing:

```powershell
winget install MongoDB.DatabaseTools
```

After install, close and reopen terminal and verify:

```powershell
mongoimport --version
```

Run `mongoexport` and `mongoimport` from terminal (PowerShell or CMD), not inside `mongosh`.

1. Export from source MongoDB:

```bash
mongoexport --uri "<source-connection-string>" --db "<database_name>" --collection "<collection_name>" --out "<collection_name>.json" --jsonArray
```

2. Import into Azure DocumentDB target:

```bash
mongoimport --uri "<target-connection-string>" --db "<database_name>" --collection "<collection_name>" --file "<collection_name>.json" --jsonArray
```

3. Repeat for each required collection.

### Option 3: `mongosh` Import from JSON

Use this if `mongoimport` is not available and JSON files are already provided.

```bash
mongosh "<target-connection-string>"
```

Then run:

```javascript
use cosmicworks

const data = JSON.parse(fs.readFileSync("sample-data/movies_with_vectors.json", "utf8"))

db.movies.insertMany(data)
```

> Option 2 and Option 3 are offline copy/import methods. They do not provide online replication state tracking or cutover orchestration.

## Part D: Monitor and Validate

Track the job until it reaches `Succeeded`.

After migration, compare source and target counts.

```javascript
use <database_name>

db.getCollectionNames().forEach(function(c) {
  print(c + ": " + db.getCollection(c).countDocuments());
});
```

Validate:

- Collections exist in the target.
- Document counts are aligned.
- Sample application queries still work.

## Part E: Online Migration and Cutover

Depending on workshop time and environment readiness, this may be run as either a participant activity or an instructor demo.

> **Watch:** [Online migration walkthrough (6:52 - 15:17)](https://youtu.be/OYtmeH0TSm4?t=410)

Key points to show:

1. Start an online migration job.
2. Observe the states: provisioning, bulk copy, replication, ready to cutover.
3. Stop write activity before cutover.
4. Execute cutover.
5. Re-run the validation query.

## Recommended Time Split

- 20 min: verify target access and connect source and target
- 30 min: pre-migration assessment
- 40 min: offline migration job setup and monitoring
- 20 min: validation
- 10 min: online migration concept and cutover demo

## Success Check

- [ ] You connected to the source MongoDB environment.
- [ ] You ran a pre-migration assessment.
- [ ] You reviewed the assessment findings.
- [ ] You created or observed an offline migration.
- [ ] You validated document counts in the target.
- [ ] You understand when online migration is needed.

## Troubleshooting

Use this quick list when commands fail during the workshop.

### 1) `mongosh` command not found

Install and verify:

```powershell
winget install MongoDB.Shell
mongosh --version
```

If this works in external CMD but not in VS Code terminal, fully close and reopen VS Code, then open a new terminal.

### 2) `mongoimport` command not found

Install MongoDB Database Tools:

```powershell
winget install MongoDB.DatabaseTools
mongoimport --version
```

If still not found, this is usually PATH refresh. Close and reopen terminal.

As a direct fallback, run by full path (adjust version folder if needed):

```cmd
"C:\Program Files\MongoDB\Tools\100\bin\mongoimport.exe" --version
```

### 3) `mongoimport` used inside `mongosh`

`mongoimport` is a terminal tool, not a `mongosh` command.

- Run `mongoimport` in CMD or PowerShell.
- Run `db.*` commands inside `mongosh`.

### 4) Connection string issues in terminal

Always wrap the full connection string in double quotes:

```bash
mongosh "<target-connection-string>"
```

Without quotes, terminal parsing can break on query string separators.

### 5) `ECONNREFUSED 127.0.0.1:27017`

This happens when running plain `mongosh` with no URI. It tries local MongoDB.

Use:

```bash
mongosh "<target-connection-string>"
```

### 6) Network timeout or authentication failed

Check in this order:

1. Cluster deployment is complete and status is running.
2. Current public IP is added under networking and saved.
3. Username/password are correct.
4. Connection string includes `tls=true` and `authMechanism=SCRAM-SHA-256`.