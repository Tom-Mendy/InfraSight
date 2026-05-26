# InfraSight Agent (Go)

Go backend agent for InfraSight AR.  
It collects machine/container metrics and streams live state to clients over WebSocket.

## Architecture

```text
infrasight-agent/
  cmd/main.go
  internal/
    collector/
      collector.go
      system.go
      docker.go
    models/
      state.go
      qr.go
    service/
      state_service.go
    transport/
      server.go
```

## What It Does

- Collects CPU and RAM usage from the machine.
- Collects Docker containers (`id`, `name`, `status`, `cpu`) when Docker is available.
- Builds a snapshot every `1s` (default).
- Broadcasts snapshots to all connected WebSocket clients on `/ws`.
- Exposes QR onboarding data and QR PNG for mobile scan flow.

## Data Model

The WebSocket sends one handshake message first:

```json
{
  "type": "connection",
  "machine_name": "laptop"
}
```

Then it streams metric snapshots. Static machine metadata is only sent in the handshake; recurring messages keep changing values only:

```json
{
  "machine": {
    "cpu": 42.1,
    "ram": 68.4
  },
  "containers": [
    {
      "id": "abc123def456",
      "name": "api",
      "status": "running",
      "cpu": 12.2
    }
  ],
  "timestamp": "2026-03-26T10:30:00Z"
}
```

## Endpoints

- `GET /ws`: WebSocket stream of live snapshots.
- `GET /state`: current latest snapshot (HTTP JSON).
- `GET /health`: health/status info.
- `GET /qr`: QR payload JSON (`ip`, `port`, `ws`).
- `GET /qr.png`: PNG QR code of connection payload (`ip`, `port`).
- `GET /scan`: official QR display page used for Android spatial tracking.

## Run

```bash
go mod tidy
go run ./cmd
```

The agent automatically loads a local `.env` file at startup.  
Use `.env.example` as the template.

## Environment Variables

- `INFRASIGHT_HOST` (default: `0.0.0.0`)
- `INFRASIGHT_PORT` (default: `8080`)
- `INFRASIGHT_NAME` (default: hostname)
- `INFRASIGHT_ADVERTISE_IP` (default: auto-detect local IPv4)
- `INFRASIGHT_INTERVAL` (default: `1s`)
- `INFRASIGHT_DOCKER_ENABLED` (default: `true`)

## Notes

- If Docker is not installed/running, the agent continues and sends an empty `containers` list.
- WebSocket origin checks are open in this Phase A implementation (local-network prototype).
