package collector

import (
	"context"
	"os"
	"testing"
	"time"

	"github.com/docker/docker/api/types/container"
	"github.com/docker/docker/api/types/network"
	gopsnet "github.com/shirou/gopsutil/v4/net"
)

func TestBytesPerSecondUsesPositiveDelta(t *testing.T) {
	got := bytesPerSecond(1024, 3072, 2)
	if got != 1024 {
		t.Fatalf("bytesPerSecond() = %v, want 1024", got)
	}
}

func TestBytesPerSecondIgnoresCounterReset(t *testing.T) {
	got := bytesPerSecond(3072, 1024, 2)
	if got != 0 {
		t.Fatalf("bytesPerSecond() = %v, want 0 for counter reset", got)
	}
}

func TestCalculateContainerCPUIgnoresCounterReset(t *testing.T) {
	var payload dockerStatsPayload
	payload.CPUStats.CPUUsage.TotalUsage = 1
	payload.PreCPUStats.CPUUsage.TotalUsage = 2
	payload.CPUStats.SystemUsage = 10
	payload.PreCPUStats.SystemUsage = 20

	got := calculateContainerCPU(payload)
	if got != 0 {
		t.Fatalf("calculateContainerCPU() = %v, want 0 for counter reset", got)
	}
}

func TestBuildContainerNetworkIndexMapsDockerIPsToContainerIDs(t *testing.T) {
	index := buildContainerNetworkIndex([]container.Summary{
		{
			ID: "abcdef1234567890",
			NetworkSettings: &container.NetworkSettingsSummary{
				Networks: map[string]*network.EndpointSettings{
					"app": {
						IPAddress: "172.20.0.2",
					},
				},
			},
		},
	})

	endpoint, ok := index["172.20.0.2"]
	if !ok {
		t.Fatal("expected container IP in network index")
	}
	if endpoint.ContainerID != "abcdef123456" || endpoint.NetworkName != "app" {
		t.Fatalf("endpoint = %+v, want short id and network name", endpoint)
	}
}

func TestBuildDockerNetworkEdgesIgnoresExternalConnections(t *testing.T) {
	index := map[string]containerNetworkEndpoint{
		"172.20.0.2": {ContainerID: "api", NetworkName: "app"},
	}
	connections := []gopsnet.ConnectionStat{
		{
			Type: 1,
			Laddr: gopsnet.Addr{
				IP:   "172.20.0.2",
				Port: 8080,
			},
			Raddr: gopsnet.Addr{
				IP:   "8.8.8.8",
				Port: 443,
			},
		},
	}

	edges := buildDockerNetworkEdges(connections, index)
	if len(edges) != 0 {
		t.Fatalf("edges = %+v, want none for external connection", edges)
	}
}

func TestBuildDockerNetworkEdgesCreatesInternalContainerEdge(t *testing.T) {
	index := map[string]containerNetworkEndpoint{
		"172.20.0.2": {ContainerID: "api", NetworkName: "app"},
		"172.20.0.3": {ContainerID: "db", NetworkName: "app"},
	}
	connections := []gopsnet.ConnectionStat{
		{
			Type:   1,
			Status: "ESTABLISHED",
			Laddr: gopsnet.Addr{
				IP:   "172.20.0.2",
				Port: 50524,
			},
			Raddr: gopsnet.Addr{
				IP:   "172.20.0.3",
				Port: 5432,
			},
		},
	}

	edges := buildDockerNetworkEdges(connections, index)
	if len(edges) != 1 {
		t.Fatalf("edge count = %d, want 1", len(edges))
	}
	edge := edges[0]
	if edge.SourceID != "api" || edge.TargetID != "db" || edge.Protocol != "tcp" || edge.State != "established" || edge.NetworkName != "app" {
		t.Fatalf("edge = %+v, want api->db tcp established app", edge)
	}
}

func TestBuildDockerNetworkEdgesDedupesSourceTargetProtocol(t *testing.T) {
	index := map[string]containerNetworkEndpoint{
		"172.20.0.2": {ContainerID: "api", NetworkName: "app"},
		"172.20.0.3": {ContainerID: "db", NetworkName: "app"},
	}
	connections := []gopsnet.ConnectionStat{
		{Type: 1, Laddr: gopsnet.Addr{IP: "172.20.0.2"}, Raddr: gopsnet.Addr{IP: "172.20.0.3"}},
		{Type: 1, Laddr: gopsnet.Addr{IP: "172.20.0.2"}, Raddr: gopsnet.Addr{IP: "172.20.0.3"}},
	}

	edges := buildDockerNetworkEdges(connections, index)
	if len(edges) != 1 {
		t.Fatalf("edge count = %d, want deduped 1", len(edges))
	}
}

func TestBuildDockerNetworkEdgesCreatesBidirectionalFixtureEdges(t *testing.T) {
	index := map[string]containerNetworkEndpoint{
		"172.24.0.2": {ContainerID: "link-a", NetworkName: "infrasight-link-test"},
		"172.24.0.3": {ContainerID: "link-b", NetworkName: "infrasight-link-test"},
	}
	connections := []gopsnet.ConnectionStat{
		{Type: 1, Status: "ESTABLISHED", Laddr: gopsnet.Addr{IP: "172.24.0.2", Port: 44000}, Raddr: gopsnet.Addr{IP: "172.24.0.3", Port: 8080}},
		{Type: 1, Status: "ESTABLISHED", Laddr: gopsnet.Addr{IP: "172.24.0.3", Port: 44001}, Raddr: gopsnet.Addr{IP: "172.24.0.2", Port: 8080}},
	}

	edges := buildDockerNetworkEdges(connections, index)
	if len(edges) != 2 {
		t.Fatalf("edge count = %d, want 2 bidirectional edges", len(edges))
	}

	if edges[0].SourceID != "link-a" || edges[0].TargetID != "link-b" {
		t.Fatalf("first edge = %+v, want link-a->link-b", edges[0])
	}
	if edges[1].SourceID != "link-b" || edges[1].TargetID != "link-a" {
		t.Fatalf("second edge = %+v, want link-b->link-a", edges[1])
	}
}

func TestParseProcNetworkConnectionsReadsContainerSocketTable(t *testing.T) {
	text := `INFRA_PROC_NET:/proc/net/tcp
  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
   0: 020018AC:C350 030018AC:1F90 01 00000000:00000000 00:00000000 00000000     0        0 1 1 0000000000000000 20 0 0 10 -1
INFRA_PROC_NET:/proc/net/udp
  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
   1: 00000000:1F90 00000000:0000 07 00000000:00000000 00:00000000 00000000     0        0 2 2 0000000000000000 0 0 0 0 -1
`

	connections := parseProcNetworkConnections(text, "link-a")
	if len(connections) != 1 {
		t.Fatalf("connection count = %d, want 1", len(connections))
	}

	connection := connections[0]
	if connection.LocalIP != "172.24.0.2" ||
		connection.LocalPort != 50000 ||
		connection.RemoteIP != "172.24.0.3" ||
		connection.RemotePort != 8080 ||
		connection.Protocol != "tcp" ||
		connection.State != "established" {
		t.Fatalf("connection = %+v, want parsed tcp socket", connection)
	}
}

func TestBuildDockerProcNetworkEdgesInfersBidirectionalPingPong(t *testing.T) {
	index := map[string]containerNetworkEndpoint{
		"172.24.0.2": {ContainerID: "link-a", NetworkName: "infrasight-link-test"},
		"172.24.0.3": {ContainerID: "link-b", NetworkName: "infrasight-link-test"},
	}
	connections := []procNetworkConnection{
		{ContainerID: "link-a", LocalIP: "172.24.0.2", LocalPort: 50000, RemoteIP: "172.24.0.3", RemotePort: 8080, Protocol: "tcp", State: "established"},
		{ContainerID: "link-b", LocalIP: "172.24.0.3", LocalPort: 8080, RemoteIP: "172.24.0.2", RemotePort: 50000, Protocol: "tcp", State: "established"},
		{ContainerID: "link-b", LocalIP: "172.24.0.3", LocalPort: 50001, RemoteIP: "172.24.0.2", RemotePort: 8080, Protocol: "tcp", State: "established"},
		{ContainerID: "link-a", LocalIP: "172.24.0.2", LocalPort: 8080, RemoteIP: "172.24.0.3", RemotePort: 50001, Protocol: "tcp", State: "established"},
	}

	edges := buildDockerProcNetworkEdges(connections, index)
	if len(edges) != 2 {
		t.Fatalf("edge count = %d, want 2 bidirectional edges", len(edges))
	}

	if edges[0].SourceID != "link-a" || edges[0].TargetID != "link-b" {
		t.Fatalf("first edge = %+v, want link-a->link-b", edges[0])
	}
	if edges[1].SourceID != "link-b" || edges[1].TargetID != "link-a" {
		t.Fatalf("second edge = %+v, want link-b->link-a", edges[1])
	}
}

func TestLiveDockerNetworkEdges(t *testing.T) {
	if os.Getenv("INFRASIGHT_DOCKER_INTEGRATION") != "1" {
		t.Skip("set INFRASIGHT_DOCKER_INTEGRATION=1 to query live Docker")
	}

	collector, err := NewDockerCollector(true)
	if err != nil {
		t.Fatalf("NewDockerCollector() error = %v", err)
	}
	defer func() {
		if closer, ok := collector.(interface{ Close() error }); ok {
			_ = closer.Close()
		}
	}()

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	edges, err := collector.CollectNetworkEdges(ctx)
	if err != nil {
		t.Fatalf("CollectNetworkEdges() error = %v", err)
	}
	for _, edge := range edges {
		t.Logf("edge %+v", edge)
	}
	if len(edges) == 0 {
		t.Fatal("CollectNetworkEdges() returned no edges")
	}
}
