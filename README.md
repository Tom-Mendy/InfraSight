# InfraSight

InfraSight is a local-network AR monitoring prototype. A Go agent runs on a
machine, collects live CPU, RAM, and Docker container state, and streams it to a
Unity AR client over WebSocket. The client connects by scanning a QR code and
renders each machine as an AR visualization.

## Current Scope

InfraSight Phase A focuses on a simple, stable local workflow:

1. Start the agent on a machine.
2. Open the AR client on a supported device.
3. Scan the agent QR code.
4. Connect to the agent over the same Wi-Fi network.
5. Visualize live machine and container state in AR.

There is no authentication in this phase. The agent is intended for trusted
local-network use.

## Repository Layout

```text
agent/                         Go WebSocket monitoring agent
client/core/                   Shared Unity package: QR, WebSocket, DTOs, orchestration
client/visuals/                Shared Unity visual prefabs and visualization components
client/android-ar/             Android AR Unity project
client/meta-quest-ar/          Meta Quest Unity project
docs/                          Project and platform architecture notes
project-assets/                Supporting project assets
```

## Agent

The agent collects machine metrics, optionally reads Docker container state, and
broadcasts snapshots every second by default.

```powershell
cd agent
go mod tidy
go run ./cmd
```

Copy `agent/.env.example` to `agent/.env` to override runtime settings:

```dotenv
INFRASIGHT_HOST=0.0.0.0
INFRASIGHT_PORT=8080
INFRASIGHT_NAME=
INFRASIGHT_ADVERTISE_IP=
INFRASIGHT_INTERVAL=1s
INFRASIGHT_DOCKER_ENABLED=true
```

### Agent Endpoints

- `GET /ws`: WebSocket stream for live client updates.
- `GET /state`: latest state snapshot as JSON.
- `GET /health`: health/status endpoint.
- `GET /qr`: QR connection payload as JSON.
- `GET /qr.png`: QR connection payload rendered as PNG.

The WebSocket contract sends an initial connection message:

```json
{
  "type": "connection",
  "machine_name": "laptop"
}
```

Then it streams snapshots:

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

If Docker is unavailable, the agent keeps running and sends an empty
`containers` list.

## Unity Clients

The Unity side is split into shared packages and platform-specific projects.
Keep platform dependencies at the edge:

- `client/core/Packages/com.infrasight.client-core`: shared QR parsing,
  WebSocket connection logic, DTOs, and orchestration.
- `client/visuals/Packages/com.infrasight.client-visuals`: shared visual
  prefabs, panels, charts, and default runtime visual components.
- `client/android-ar/`: Android AR project using AR Foundation/ARCore and
  ZXing-based QR scanning.
- `client/meta-quest-ar/`: Meta Quest project using the Meta/MRUK QR path.

The core package must stay independent from Meta, ARCore, AR Foundation, and
ZXing-specific code.

### Android AR

Open `client/android-ar` in Unity. This project is for non-Meta Android AR
builds.

Expected build symbol:

```text
INFRASIGHT_ANDROID_AR
```

Quick compile check:

```powershell
cd client/android-ar
dotnet build .\infrasight-client-android-ar.sln --no-restore
```

### Meta Quest

Open `client/meta-quest-ar` in Unity. This project keeps the Meta SDK/MRUK
integration and Quest-specific manifest requirements.

Expected build symbol:

```text
INFRASIGHT_META_QUEST
```

Quick compile check:

```powershell
cd client/meta-quest-ar
dotnet build .\infrasight-client.sln --no-restore
```

## QR Connection

The QR flow is the normal client onboarding path. The agent exposes the payload
through `/qr` and `/qr.png`; the Unity client scans it, builds the WebSocket
endpoint, and connects automatically.

Example LAN payload:

```json
{
  "name": "Local Agent",
  "ip": "192.168.1.42",
  "port": 8080
}
```

The client also supports direct WebSocket URL payloads:

```text
ws://192.168.1.42:8080/ws
```

For device testing, replace the IP address with the host machine's LAN address.
`127.0.0.1` only works when the client runs on the same machine as the agent.

## Validation

Useful checks before committing:

```powershell
cd agent
go test ./...

cd ..\client\android-ar
dotnet build .\infrasight-client-android-ar.sln --no-restore

cd ..\meta-quest-ar
dotnet build .\infrasight-client.sln --no-restore

cd ..\..
git diff --check
```

## Documentation

- [Project document](docs/Infra%20Sight%20-%20Project%20Document.md)
- [Unity client platform architecture](docs/unity-client-platform-architecture.md)
- [Agent README](agent/README.md)
