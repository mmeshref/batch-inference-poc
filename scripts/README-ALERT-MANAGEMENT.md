# Alert Management Scripts

Simple scripts to manage Prometheus/Alertmanager alerts for testing and troubleshooting.

## Scripts

### 1. `clear-all-alerts.sh`
**Clears all firing alerts by removing all silences.**

```bash
./scripts/clear-all-alerts.sh
```

- Deletes all active silences in Alertmanager
- Alerts will automatically resolve when their conditions are no longer met
- No parameters needed - just run it

### 2. `simulate-target-down.sh`
**Simulates a service going down to trigger alerts.**

```bash
# Scale down api-gateway (default)
./scripts/simulate-target-down.sh

# Or specify a service
./scripts/simulate-target-down.sh scheduler
./scripts/simulate-target-down.sh gpu-worker-spot
./scripts/simulate-target-down.sh gpu-worker-dedicated
```

- Scales the specified deployment to 0 replicas
- Prometheus will detect the target is down
- TargetDown alerts will fire after ~1 minute

### 3. `restore-target-up.sh`
**Restores services back to normal - alerts will auto-resolve.**

```bash
# Restore a specific service
./scripts/restore-target-up.sh api-gateway
./scripts/restore-target-up.sh scheduler
./scripts/restore-target-up.sh gpu-worker-spot
./scripts/restore-target-up.sh gpu-worker-dedicated

# Restore all services
./scripts/restore-target-up.sh all
```

- Scales services back to their normal replica counts
- Once targets are healthy, alerts automatically resolve
- No manual intervention needed - Prometheus handles resolution

## How Auto-Resolution Works

The `TargetDown` alert rule is configured as:
```yaml
- alert: TargetDown
  expr: up == 0
  for: 1m
```

**How it works:**
1. **Firing**: When `up == 0` is true for 1 minute, the alert fires
2. **Auto-Resolution**: When `up == 1` (target is back up), the condition `up == 0` becomes false
3. Prometheus automatically marks the alert as "resolved" - no manual action needed

**Timeline:**
- Target goes down → `up` becomes `0`
- After 1 minute → Alert fires (status: "firing")
- Target comes back up → `up` becomes `1`
- Within seconds → Alert automatically resolves (status: "resolved")

## Viewing Alerts

### Prometheus UI
```bash
kubectl port-forward -n batch-inference svc/prometheus 9090:9090
```
Open: http://localhost:9090/alerts

### Alertmanager UI
```bash
kubectl port-forward -n batch-inference svc/alertmanager 9093:9093
```
Open: http://localhost:9093

## Example Workflow

```bash
# 1. Simulate api-gateway going down
./scripts/simulate-target-down.sh api-gateway

# 2. Wait ~1 minute, then check alerts in Prometheus UI
# You should see TargetDown alert firing

# 3. Restore the service
./scripts/restore-target-up.sh api-gateway

# 4. Wait ~30 seconds, check Prometheus UI again
# Alert should automatically show as "resolved"

# 5. If alerts are stuck, clear all silences
./scripts/clear-all-alerts.sh
```

## Troubleshooting

**Alerts not resolving?**
- Check that the target is actually up: `kubectl get pods -n batch-inference`
- Verify Prometheus can scrape: Check `/targets` in Prometheus UI
- Clear silences: `./scripts/clear-all-alerts.sh`

**Alerts not firing?**
- Wait at least 1 minute after scaling down (alert threshold)
- Check Prometheus is scraping: `kubectl logs -n batch-inference -l app=prometheus`
- Verify alert rules are loaded: Check `/rules` in Prometheus UI
