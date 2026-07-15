# High Availability Showcase — Kill the Primary, Nothing Is Lost

This is the core differentiator vs. InfluxDB OSS (no clustering) and the
"operator assembly required" story of PostgreSQL/Timescale HA.

## Setup (3 terminals + browser)

1. **Browser**: Ops Manager → Projects → cement-plant → Deployment
   (shows live topology: which member is PRIMARY).
2. **Terminal 1**: `k9s -n mongodb-operator` (live pod view).
3. **Terminal 2**: the ingestion client, running **in-cluster** for
   transparent failover:
   ```bash
   ../4-dotnet-client/run-in-cluster.sh
   ```

Find the current primary (also visible in the Ops Manager topology view):

```bash
kubectl exec -n mongodb-operator replica-set-0 -c mongodb-enterprise-database -- \
  /var/lib/mongodb-mms-automation/mongodb-linux-*/bin/mongosh --quiet \
  --eval 'db.hello().primary' 2>/dev/null \
|| echo "use the Ops Manager UI to identify the primary"
```

## Act 1 — kill a secondary (warm-up)

```bash
kubectl delete pod replica-set-2 -n mongodb-operator
```

- Ingestion continues, zero interruption.
- Ops Manager flags the member as down; the operator/StatefulSet recreates
  the pod; it rejoins and catches up automatically.

## Act 2 — kill the PRIMARY (the money shot)

```bash
kubectl delete pod replica-set-0 -n mongodb-operator   # assuming 0 is primary
```

What the customer sees:

1. The remaining two members **elect a new primary within seconds**.
2. The .NET client (retryable writes + replica set connection string)
   keeps ingesting — at most a ~2s blip, **no data loss, no manual action**.
3. Kubernetes recreates `replica-set-0`; it rejoins **as a secondary** and
   resyncs from the new primary.
4. Ops Manager shows the whole story on the Deployment page: election,
   member states, replication lag returning to zero.

Verify no data was lost — total count keeps increasing:

```bash
cd ../4-dotnet-client && dotnet run -- query
```
(requires the port-forward from the main README, step 5)

## Act 3 — declarative scaling (optional, 30 seconds)

Edit `3-replica-set/replica-set.yaml`, change `members: 3` → `members: 5`,
then:

```bash
kubectl apply -f ../3-replica-set/replica-set.yaml
```

Two new members appear, are configured by Ops Manager automation, and join
the replica set — no scripts, no downtime.

## Closing line

> "Everything you just saw — deployment, monitoring, failover, scaling —
> came from ~60 lines of YAML and one operator, running fully on-prem.
> This is the HA story that InfluxDB OSS doesn't have, without assembling
> and operating a third-party Postgres operator stack."
