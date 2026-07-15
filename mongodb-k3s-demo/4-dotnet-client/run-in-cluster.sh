#!/usr/bin/env bash
# Runs the .NET telemetry client inside the k8s cluster with the full
# replica set connection string (transparent failover).
set -euo pipefail
cd "$(dirname "$0")"

kubectl create configmap plant-telemetry-src -n mongodb-operator \
  --from-file=Program.cs --from-file=PlantTelemetry.csproj \
  --dry-run=client -o yaml | kubectl apply -f -

kubectl delete pod plant-telemetry -n mongodb-operator --ignore-not-found
kubectl apply -f ingest-pod.yaml

echo "Waiting for pod (first run downloads the SDK image, ~1 min)..."
kubectl wait --for=condition=Ready pod/plant-telemetry -n mongodb-operator --timeout=300s
kubectl logs -f plant-telemetry -n mongodb-operator
