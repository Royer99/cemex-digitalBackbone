#!/usr/bin/env bash
# Wires the operator to Ops Manager with ZERO manual UI steps:
# - reuses the global admin API key that MCK creates automatically
#   (secret: mongodb-operator-ops-manager-admin-key)
# - creates (or reuses) the organization via the OM public API
# - creates the credentials secret + project ConfigMap
#
# Requires Ops Manager reachable on localhost:8080, i.e. in another terminal:
#   kubectl port-forward service/ops-manager-svc 8080:8080 -n mongodb-operator
set -euo pipefail

NS=mongodb-operator
PROJECT=cement-plant
OM_URL=http://localhost:8080

PUB=$(kubectl get secret mongodb-operator-ops-manager-admin-key -n $NS -o jsonpath='{.data.publicKey}' | base64 -d)
PRIV=$(kubectl get secret mongodb-operator-ops-manager-admin-key -n $NS -o jsonpath='{.data.privateKey}' | base64 -d)

kubectl create secret generic organization-secret -n $NS \
  --from-literal=user="$PUB" --from-literal=publicApiKey="$PRIV" \
  --dry-run=client -o yaml | kubectl apply -f -

ORG_ID=$(curl -sf -u "$PUB:$PRIV" --digest "$OM_URL/api/public/v1.0/orgs" \
  | python3 -c "import sys,json; o=[x['id'] for x in json.load(sys.stdin)['results'] if x['name']=='$PROJECT']; print(o[0] if o else '')")

if [ -z "$ORG_ID" ]; then
  ORG_ID=$(curl -sf -u "$PUB:$PRIV" --digest -X POST -H "Content-Type: application/json" \
    -d "{\"name\":\"$PROJECT\"}" "$OM_URL/api/public/v1.0/orgs" \
    | python3 -c "import sys,json; print(json.load(sys.stdin)['id'])")
  echo "Created organization '$PROJECT' (orgId=$ORG_ID)"
else
  echo "Reusing organization '$PROJECT' (orgId=$ORG_ID)"
fi

kubectl create configmap cement-plant-project -n $NS \
  --from-literal=baseUrl=http://ops-manager-svc.$NS.svc.cluster.local:8080 \
  --from-literal=projectName=$PROJECT \
  --from-literal=orgId="$ORG_ID" \
  --dry-run=client -o yaml | kubectl apply -f -

echo "Done. Deploy the replica set with: kubectl apply -f replica-set.yaml"
