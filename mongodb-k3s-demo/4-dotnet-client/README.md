# .NET Cement Plant Telemetry Client

Console app (MongoDB .NET driver) that simulates sensors across a cement
plant — rotary kiln, preheater tower, clinker cooler, raw/cement mills,
hoppers and silos — and streams one reading per sensor per second into a
MongoDB **time series collection** (`cement_plant.telemetry`, timeField `ts`,
metaField `sensor`).

## Run locally (via port-forward)

```bash
# terminal 1 — expose the replica set
kubectl port-forward -n mongodb-operator svc/replica-set-svc 27017:27017

# terminal 2 — ingest
dotnet run

# terminal 3 — analytics (per-equipment averages, kiln temperature trend)
dotnet run -- query
```

Connection string can be overridden with the `MONGODB_URI` env var
(default: `mongodb://localhost:27017/?directConnection=true`).

## Run inside the cluster (transparent failover)

Port-forward pins the client to a single pod, so a primary failover
requires re-establishing the tunnel. Real applications connect with the
replica set connection string and fail over automatically. To demo that:

```bash
./run-in-cluster.sh
```

This ships the source into a ConfigMap and runs it in a `dotnet/sdk` pod
with the full 3-member connection string + `retryWrites=true`. Then kill
the primary pod and watch ingestion continue uninterrupted.

## Example document

```json
{
  "ts": { "$date": "2026-07-14T16:20:11Z" },
  "sensor": { "plant": "monterrey", "area": "pyroprocessing",
              "equipment": "kiln-1", "type": "rotary_kiln" },
  "burningZoneTempC": 1451.3,
  "shellTempC": 311.9,
  "rpm": 3.48
}
```

Different equipment types carry different measurement fields — no schema
migrations needed to onboard a new sensor type.
