# MongoDB On-Prem Demo — Ops Manager + 3-Node Replica Set on k3s (k3d)

Demo showing how easy it is to run a production-grade MongoDB deployment
on-premises with Kubernetes, using:

- **k3d** — lightweight k3s (Kubernetes) running in Docker
- **MCK** — MongoDB Controllers for Kubernetes (the official operator)
- **Ops Manager** — enterprise monitoring, automation and backup UI
- **3-node replica set** — automatic failover / high availability
- **.NET client** — ingesting time-series telemetry from a simulated cement plant

Why this matters vs. the InfluxDB/TimescaleDB comparison (`../comparison.md`):

| Concern from the comparison | MongoDB answer in this demo |
|---|---|
| InfluxDB OSS has no clustering, HA is DIY | Native replica set: automatic election and failover, shown live |
| Operator-driven HA (Timescale needs Zalando/Crunchy) | One official operator (MCK) deploys DB **and** the ops tooling |
| Time-series workloads | Native time series collections with columnar compression |
| .NET/EF ecosystem | Official MongoDB .NET driver (+ EF Core provider available) |

## Prerequisites

- [Docker](https://www.docker.com/) — give it **≥ 15 GB RAM / 10 CPUs** (Settings → Resources)
- [k3d](https://k3d.io/stable/) — `brew install k3d`
- [kubectl](https://kubernetes.io/docs/tasks/tools/) — `brew install kubectl`
- [Helm](https://helm.sh/) — `brew install helm`
- [k9s](https://k9scli.io/) (optional, great for showing pods live) — `brew install k9s`
- [.NET SDK 7+](https://dotnet.microsoft.com/download) for the ingestion client
- [MongoDB Compass](https://www.mongodb.com/products/tools/compass) (optional, for browsing data)

> **Timing tip:** steps 1–3 take ~15–20 minutes (image pulls + Ops Manager
> startup). Run them **before** the customer meeting and start the live demo
> at step 4.

## 1. Create the Kubernetes cluster

**Option A — Linux server / EC2 (Ubuntu 24.04, recommended for the customer demo):**

```bash
./0-cluster/bootstrap-ubuntu24.sh   # installs k3s, helm, .NET SDK, mongosh, k9s
```

This runs real k3s directly on the host — no Docker needed. Then skip to step 2.

**Option B — laptop (k3d = k3s in Docker):**

```bash
k3d cluster create mongodb-demo
kubectl get nodes   # verify
```

## 2. Install the MongoDB operator (MCK)

```bash
helm repo add mongodb https://mongodb.github.io/helm-charts
helm repo update

helm install kubernetes-operator mongodb/mongodb-kubernetes \
  --namespace mongodb-operator \
  --create-namespace \
  --set operator.env=dev
```

Verify: `kubectl get pods -n mongodb-operator` shows the operator running.

## 3. Deploy Ops Manager (+ 3-member AppDB)

```bash
kubectl apply -f 2-ops-manager/deploy-om.yaml
```

This pulls ~2 GB of images and takes **10–15 minutes**. Watch progress with
`k9s -n mongodb-operator`. You'll end up with:

- `ops-manager-db-0/1/2` — Ops Manager's own backing replica set (AppDB)
- `ops-manager-0` — the Ops Manager application

When ready, expose the UI (leave running in its own terminal):

```bash
kubectl port-forward service/ops-manager-svc 8080:8080 -n mongodb-operator
```

Open http://localhost:8080 and log in with the admin credentials from
`2-ops-manager/deploy-om.yaml` (`om-admin` / see the secret).

## 4. Wire the operator to Ops Manager (fully automated)

MCK automatically creates a global-owner API key when it bootstraps Ops
Manager (secret `mongodb-operator-ops-manager-admin-key`). With the OM
port-forward from step 3 still running:

```bash
./3-replica-set/wire-credentials.sh
```

This reuses that key, creates the `cement-plant` organization via the OM API
(`orgId` is mandatory in the project ConfigMap), and creates the credentials
secret — no UI steps needed.

<details>
<summary>Manual alternative (via the OM UI)</summary>

1. **Organization → Access Manager → Create API Key** (role: *Organization Owner*).
2. Add the operator pod's IP (or the pod CIDR `10.42.0.0/16`) to the key's
   **API Access List**.
3. Copy the keys into `3-replica-set/secret.yaml` and the org ID into
   `3-replica-set/config-map.yaml`, then `kubectl apply -f` both files.
</details>

## 5. Deploy the 3-node replica set

```bash
kubectl apply -f 3-replica-set/replica-set.yaml
```

Watch the three pods come up (`replica-set-0/1/2`), then check Ops Manager:
**Projects → cement-plant → Deployment** — the replica set appears with full
monitoring, metrics and topology, automatically.

Expose it for local clients (leave running in its own terminal). Forward the
**primary pod** directly — forwarding the service can land on a secondary,
which rejects writes. Find the primary in the OM Deployment view (or via
`db.hello().primary` from mongosh), then:

```bash
kubectl port-forward -n mongodb-operator pod/replica-set-0 27017:27017
```

> If a local mongod already occupies 27017 (e.g. Homebrew), forward to
> another port (`27018:27017`) and set
> `MONGODB_URI="mongodb://localhost:27018/?directConnection=true"` for the
> client. In-cluster apps don't have these limitations — they use the full
> replica set connection string (see `4-dotnet-client/run-in-cluster.sh`).

## 6. Ingest cement plant telemetry with the .NET client

```bash
cd 4-dotnet-client
dotnet run                 # starts simulating kiln/mill/hopper sensors
```

The client creates a **time series collection** (`cement_plant.telemetry`)
and streams readings from simulated equipment (rotary kiln, cement mill, raw
mill, clinker cooler, hoppers). In a second terminal:

```bash
dotnet run -- query        # live analytics: per-equipment averages, kiln trend
```

Browse the data in Compass: `mongodb://localhost:27017/?directConnection=true`

## 7. Showcase high availability (the money shot)

See [5-showcase/failover.md](5-showcase/failover.md). Summary: kill the
primary pod while the .NET client keeps writing — the replica set elects a
new primary in seconds, the operator recreates the pod, ingestion continues.
This is exactly what InfluxDB OSS cannot do.

## Talking points

- **One operator, everything declarative** — the whole deployment is a handful
  of YAML files; scaling to 5 members is a one-line change (`members: 5`).
- **Ops Manager on-prem** — monitoring, alerting, automated backups and
  upgrades without any cloud dependency.
- **Time series collections** — purpose-built storage (compression, bucketing)
  with full MongoDB query language, aggregation and secondary indexes; no
  second database needed for business/relational data.
- **Path to production** — same manifests work on real multi-node k3s/RKE2/
  OpenShift; add TLS, SCRAM auth and backup (reference repo covers all three).

## Not covered (kept out of scope on purpose)

- SCRAM authentication & database users
- TLS
- Backup, sharding, multi-cluster

## Cleanup

```bash
k3d cluster delete mongodb-demo
```
