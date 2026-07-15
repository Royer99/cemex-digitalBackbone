#!/usr/bin/env bash
# Bootstrap an Ubuntu 24.04 LTS host (e.g. EC2) with everything needed to
# run this demo: k3s (incl. kubectl), helm, .NET SDK 8, mongosh, k9s.
# Run as a regular user with sudo privileges:  ./bootstrap-ubuntu24.sh
set -euo pipefail

if [ "$(id -u)" -eq 0 ]; then
  echo "Run as a regular user with sudo, not as root." >&2
  exit 1
fi

ARCH=$(dpkg --print-architecture)   # amd64 or arm64

echo "==> apt packages (curl, git, .NET SDK 8)"
sudo apt-get update -y
sudo apt-get install -y curl git gnupg ca-certificates dotnet-sdk-8.0

echo "==> k3s (lightweight Kubernetes, includes kubectl)"
if ! command -v k3s >/dev/null; then
  curl -sfL https://get.k3s.io | sh -
fi
mkdir -p "$HOME/.kube"
sudo cp /etc/rancher/k3s/k3s.yaml "$HOME/.kube/config"
sudo chown "$USER" "$HOME/.kube/config"
chmod 600 "$HOME/.kube/config"
grep -q 'KUBECONFIG=' "$HOME/.bashrc" || echo 'export KUBECONFIG=$HOME/.kube/config' >> "$HOME/.bashrc"
export KUBECONFIG="$HOME/.kube/config"

echo "==> helm"
if ! command -v helm >/dev/null; then
  curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
fi

echo "==> mongosh (MongoDB shell)"
if ! command -v mongosh >/dev/null; then
  curl -fsSL https://www.mongodb.org/static/pgp/server-8.0.asc \
    | sudo gpg --dearmor -o /usr/share/keyrings/mongodb-server-8.0.gpg
  echo "deb [ arch=amd64,arm64 signed-by=/usr/share/keyrings/mongodb-server-8.0.gpg ] https://repo.mongodb.org/apt/ubuntu noble/mongodb-org/8.0 multiverse" \
    | sudo tee /etc/apt/sources.list.d/mongodb-org-8.0.list >/dev/null
  sudo apt-get update -y
  sudo apt-get install -y mongodb-mongosh
fi

echo "==> k9s (optional cluster TUI)"
if ! command -v k9s >/dev/null; then
  curl -fsSL "https://github.com/derailed/k9s/releases/latest/download/k9s_Linux_${ARCH}.tar.gz" \
    | sudo tar -xz -C /usr/local/bin k9s
fi

echo
echo "==> Verification"
kubectl get nodes
helm version --short
dotnet --version
mongosh --version
k9s version --short || true

echo
echo "Bootstrap complete. Next steps (from the mongodb-k3s-demo folder):"
echo "  1. Install the operator     -> README step 2 (helm install)"
echo "  2. Deploy Ops Manager       -> kubectl apply -f 2-ops-manager/deploy-om.yaml"
echo "  3. Wire credentials         -> ./3-replica-set/wire-credentials.sh"
echo "  4. Deploy the replica set   -> kubectl apply -f 3-replica-set/replica-set.yaml"
