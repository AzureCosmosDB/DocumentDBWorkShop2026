const { AzureCliCredential } = require("@azure/identity");
const { MongoClient } = require("mongodb");

const documentDbScope = "https://ossrdbms-aad.database.windows.net/.default";

function requiredEnvironmentVariable(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Missing ${name}. Run the Module 1 setup script first.`);
  return value;
}

function createOrders(count = 10000) {
  const statuses = ["shipped", "pending", "delivered", "cancelled", "processing"];
  const customers = Array.from({ length: 10 }, (_, index) => `C-${1001 + index}`);
  const skus = ["SKU-A1", "SKU-B2", "SKU-C3", "SKU-D4", "SKU-E5", "SKU-F6", "SKU-G7", "SKU-H8"];
  const start = Date.UTC(2023, 0, 1);
  return Array.from({ length: count }, (_, index) => ({
    _id: `O-${String(index + 1).padStart(5, "0")}`,
    customerId: customers[index % customers.length],
    status: statuses[(index * 7) % statuses.length],
    total: ((index * 7919) % 99900 + 100) / 100,
    createdAt: new Date(start + ((index * 37) % 730) * 86400000),
    items: [{ sku: skus[(index * 3) % skus.length], qty: (index % 5) + 1 }]
  }));
}

async function explainFind(database, filter, sort) {
  return database.command({
    explain: { find: "orders", filter, sort },
    verbosity: "executionStats"
  });
}

function summarizeExplain(label, explain) {
  const stats = explain.executionStats ?? explain;
  console.table([{
    plan: label,
    stage: explain.queryPlanner?.winningPlan?.stage ?? "see full plan",
    nReturned: stats.nReturned,
    totalDocsExamined: stats.totalDocsExamined,
    totalKeysExamined: stats.totalKeysExamined,
    executionTimeMillis: stats.executionTimeMillis
  }]);
}

async function main() {
  const clusterName = requiredEnvironmentVariable("DOCUMENTDB_CLUSTER_NAME");
  const credential = new AzureCliCredential();
  const client = new MongoClient(
    `mongodb+srv://${clusterName}.global.mongocluster.cosmos.azure.com/`,
    {
      tls: true,
      retryWrites: false,
      authMechanism: "MONGODB-OIDC",
      authMechanismProperties: {
        OIDC_CALLBACK: async () => {
          const token = await credential.getToken(documentDbScope);
          return {
            accessToken: token.token,
            expiresInSeconds: Math.max(0, Math.floor((token.expiresOnTimestamp - Date.now()) / 1000))
          };
        },
        ALLOWED_HOSTS: ["*.azure.com"]
      }
    }
  );

  try {
    await client.connect();
    const database = client.db("docdbworkshop");
    const customers = database.collection("customers");
    const orders = database.collection("orders");
    const demo = database.collection("demo");

    console.log("1. Loading deterministic workshop data");
    await Promise.all([customers.deleteMany({}), orders.deleteMany({}), demo.deleteMany({})]);
    await customers.insertMany(Array.from({ length: 10 }, (_, index) => ({
      _id: `C-${1001 + index}`,
      name: `Workshop Customer ${index + 1}`,
      tier: ["gold", "silver", "bronze"][index % 3],
      region: ["eastus", "westus", "central"][index % 3]
    })));
    const generatedOrders = createOrders();
    for (let offset = 0; offset < generatedOrders.length; offset += 1000) {
      await orders.insertMany(generatedOrders.slice(offset, offset + 1000));
    }
    console.log({ customers: await customers.countDocuments(), orders: await orders.countDocuments() });

    console.log("\n2. Comparing embedded and polymorphic models");
    await demo.insertMany([
      { _id: "DEMO-001", type: "embedded_order", customerId: "C-1001", total: 79.90,
        items: [{ sku: "BOOK-101", qty: 1 }, { sku: "BOOK-202", qty: 1 }] },
      { _id: "DEMO-002", orderId: "DEMO-002", type: "order", customerId: "C-1001", total: 79.90 },
      { _id: "DEMO-002-1", orderId: "DEMO-002", type: "line_item", sku: "BOOK-101", qty: 1 },
      { _id: "DEMO-002-2", orderId: "DEMO-002", type: "line_item", sku: "BOOK-202", qty: 1 }
    ]);
    console.log({ embeddedDocuments: await demo.countDocuments({ _id: "DEMO-001" }), polymorphicDocuments: await demo.countDocuments({ orderId: "DEMO-002" }) });

    console.log("\n3. Exploring status distribution");
    console.table(await orders.aggregate([{ $group: { _id: "$status", count: { $sum: 1 } } }, { $sort: { count: -1 } }]).toArray());

    console.log("\n4. Comparing a baseline with an ESR index");
    await orders.dropIndexes();
    const financeFilter = { status: "delivered", total: { $gte: 500 } };
    summarizeExplain("baseline", await explainFind(database, financeFilter, { total: -1 }));
    await orders.createIndex({ status: 1, total: -1 }, { name: "status_1_total_-1" });
    summarizeExplain("ESR index", await explainFind(database, financeFilter, { total: -1 }));

    console.log("\n5. Testing a distinct sort and range field");
    await orders.dropIndexes();
    const customerFilter = { customerId: "C-1006", status: "shipped", total: { $gte: 100 } };
    summarizeExplain("baseline", await explainFind(database, customerFilter, { createdAt: -1 }));
    await orders.createIndex({ customerId: 1, status: 1, createdAt: -1, total: 1 }, { name: "customer_status_date_total" });
    summarizeExplain("ESR index", await explainFind(database, customerFilter, { createdAt: -1 }));

    console.log("\n6. Aggregating the shipped-order leaderboard");
    const leaderboard = await orders.aggregate([
      { $match: { status: "shipped", createdAt: { $gte: new Date("2024-01-01") } } },
      { $group: { _id: "$customerId", totalRevenue: { $sum: "$total" }, orderCount: { $sum: 1 } } },
      { $sort: { totalRevenue: -1 } },
      { $limit: 5 }
    ]).toArray();
    console.table(leaderboard);
    console.log("Success: compare the two explain tables and discuss which work each index removes.");
  } finally {
    await client.close();
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});