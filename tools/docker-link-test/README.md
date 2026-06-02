# InfraSight Docker Link Test

Small two-container fixture for validating Docker peer-to-peer network edges.

## Run

```powershell
docker compose -f tools/docker-link-test/docker-compose.yml up --build
```

## Stop

```powershell
docker compose -f tools/docker-link-test/docker-compose.yml down -v
```

Expected behavior:

- `infrasight-link-a` serves HTTP and repeatedly requests `infrasight-link-b`.
- `infrasight-link-b` serves HTTP and repeatedly requests `infrasight-link-a`.
- InfraSight agent should report `network_edges` in both directions when Docker socket visibility allows it.
