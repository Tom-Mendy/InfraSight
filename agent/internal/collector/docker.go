package collector

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"sort"
	"strconv"
	"strings"
	"time"

	"infrasight-agent/internal/models"

	"github.com/docker/docker/api/types/container"
	"github.com/docker/docker/client"
	"github.com/docker/docker/pkg/stdcopy"
	gopsnet "github.com/shirou/gopsutil/v4/net"
)

const (
	dockerListTimeout  = 2 * time.Second
	dockerStatsTimeout = 1500 * time.Millisecond
	dockerExecTimeout  = 1500 * time.Millisecond
	ephemeralPortFloor = 32768
)

type noopDockerCollector struct{}

func (noopDockerCollector) CollectContainers(context.Context) ([]models.Container, error) {
	return []models.Container{}, nil
}

func (noopDockerCollector) CollectNetworkEdges(context.Context) ([]models.NetworkEdge, error) {
	return []models.NetworkEdge{}, nil
}

type DockerCollector struct {
	client              *client.Client
	previousNetworkStat map[string]containerNetworkSample
}

func NewDockerCollector(enabled bool) (ContainerCollector, error) {
	if !enabled {
		return noopDockerCollector{}, nil
	}

	cli, err := client.NewClientWithOpts(client.FromEnv, client.WithAPIVersionNegotiation())
	if err != nil {
		return nil, fmt.Errorf("create docker client: %w", err)
	}

	return &DockerCollector{
		client:              cli,
		previousNetworkStat: make(map[string]containerNetworkSample),
	}, nil
}

func (c *DockerCollector) Close() error {
	if c.client == nil {
		return nil
	}
	return c.client.Close()
}

func (c *DockerCollector) CollectContainers(parent context.Context) ([]models.Container, error) {
	if c.client == nil {
		return []models.Container{}, nil
	}

	listCtx, cancel := context.WithTimeout(parent, dockerListTimeout)
	defer cancel()

	list, err := c.client.ContainerList(listCtx, container.ListOptions{All: true})
	if err != nil {
		if isDockerUnavailableError(err) {
			return []models.Container{}, nil
		}
		return nil, fmt.Errorf("list docker containers: %w", err)
	}

	out := make([]models.Container, 0, len(list))
	activeContainerIDs := make(map[string]struct{}, len(list))
	for _, item := range list {
		activeContainerIDs[item.ID] = struct{}{}

		status := normalizeContainerStatus(item.State, item.Status)
		containerMetrics := dockerContainerMetrics{
			NetworkNames: containerNetworkNames(item),
		}

		if status == "running" {
			statsCtx, statsCancel := context.WithTimeout(parent, dockerStatsTimeout)
			metrics, statsErr := c.collectContainerMetrics(statsCtx, item.ID)
			statsCancel()
			if statsErr == nil {
				containerMetrics = metrics
				if len(containerMetrics.NetworkNames) == 0 {
					containerMetrics.NetworkNames = containerNetworkNames(item)
				}
			}
		}

		out = append(out, models.Container{
			ID:               shortID(item.ID),
			Name:             containerName(item.Names, item.ID),
			Status:           status,
			CPU:              containerMetrics.CPU,
			RXBytes:          containerMetrics.RXBytes,
			TXBytes:          containerMetrics.TXBytes,
			RXBytesPerSecond: containerMetrics.RXBytesPerSecond,
			TXBytesPerSecond: containerMetrics.TXBytesPerSecond,
			NetworkNames:     containerMetrics.NetworkNames,
		})
	}

	c.removeStaleNetworkSamples(activeContainerIDs)

	sort.Slice(out, func(i, j int) bool {
		return out[i].Name < out[j].Name
	})
	return out, nil
}

func (c *DockerCollector) CollectNetworkEdges(parent context.Context) ([]models.NetworkEdge, error) {
	if c.client == nil {
		return []models.NetworkEdge{}, nil
	}

	listCtx, cancel := context.WithTimeout(parent, dockerListTimeout)
	defer cancel()

	list, err := c.client.ContainerList(listCtx, container.ListOptions{All: true})
	if err != nil {
		if isDockerUnavailableError(err) {
			return []models.NetworkEdge{}, nil
		}
		return nil, fmt.Errorf("list docker containers for network edges: %w", err)
	}

	networkIndex := buildContainerNetworkIndex(list)
	if len(networkIndex) == 0 {
		return []models.NetworkEdge{}, nil
	}

	if connections, err := gopsnet.ConnectionsWithContext(parent, "inet"); err == nil {
		if edges := buildDockerNetworkEdges(connections, networkIndex); len(edges) > 0 {
			return edges, nil
		}
	}

	return c.collectDockerProcNetworkEdges(parent, list, networkIndex), nil
}

func (c *DockerCollector) collectDockerProcNetworkEdges(
	parent context.Context,
	list []container.Summary,
	networkIndex map[string]containerNetworkEndpoint,
) []models.NetworkEdge {
	connections := make([]procNetworkConnection, 0)
	for _, item := range list {
		if normalizeContainerStatus(item.State, item.Status) != "running" {
			continue
		}

		ctx, cancel := context.WithTimeout(parent, dockerExecTimeout)
		output, err := c.readContainerProcNetwork(ctx, item.ID)
		cancel()
		if err != nil {
			continue
		}

		connections = append(connections, parseProcNetworkConnections(output, shortID(item.ID))...)
	}

	return buildDockerProcNetworkEdges(connections, networkIndex)
}

func (c *DockerCollector) readContainerProcNetwork(ctx context.Context, containerID string) (string, error) {
	exec, err := c.client.ContainerExecCreate(ctx, containerID, container.ExecOptions{
		AttachStdout: true,
		AttachStderr: true,
		Cmd: []string{
			"sh",
			"-c",
			`for f in /proc/net/tcp /proc/net/udp /proc/net/tcp6 /proc/net/udp6; do echo "INFRA_PROC_NET:$f"; cat "$f" 2>/dev/null; done`,
		},
	})
	if err != nil {
		return "", err
	}

	response, err := c.client.ContainerExecAttach(ctx, exec.ID, container.ExecAttachOptions{})
	if err != nil {
		return "", err
	}
	defer response.Close()

	var stdout bytes.Buffer
	if _, err := stdcopy.StdCopy(&stdout, io.Discard, response.Reader); err != nil {
		return "", err
	}

	return stdout.String(), nil
}

func (c *DockerCollector) collectContainerMetrics(ctx context.Context, containerID string) (dockerContainerMetrics, error) {
	stats, err := c.client.ContainerStats(ctx, containerID, false)
	if err != nil {
		return dockerContainerMetrics{}, err
	}
	defer stats.Body.Close()

	var payload dockerStatsPayload
	if err := json.NewDecoder(stats.Body).Decode(&payload); err != nil {
		return dockerContainerMetrics{}, err
	}

	metrics := dockerContainerMetrics{
		CPU:          calculateContainerCPU(payload),
		NetworkNames: make([]string, 0, len(payload.Networks)),
	}

	for networkName, network := range payload.Networks {
		metrics.RXBytes += network.RXBytes
		metrics.TXBytes += network.TXBytes
		metrics.NetworkNames = append(metrics.NetworkNames, networkName)
	}
	sort.Strings(metrics.NetworkNames)

	now := time.Now()
	if !payload.Read.IsZero() {
		now = payload.Read
	}

	if previous, ok := c.previousNetworkStat[containerID]; ok {
		elapsedSeconds := now.Sub(previous.ReadAt).Seconds()
		if elapsedSeconds > 0 {
			metrics.RXBytesPerSecond = bytesPerSecond(previous.RXBytes, metrics.RXBytes, elapsedSeconds)
			metrics.TXBytesPerSecond = bytesPerSecond(previous.TXBytes, metrics.TXBytes, elapsedSeconds)
		}
	}

	c.previousNetworkStat[containerID] = containerNetworkSample{
		RXBytes: metrics.RXBytes,
		TXBytes: metrics.TXBytes,
		ReadAt:  now,
	}

	return metrics, nil
}

func calculateContainerCPU(payload dockerStatsPayload) float64 {
	if payload.CPUStats.CPUUsage.TotalUsage < payload.PreCPUStats.CPUUsage.TotalUsage ||
		payload.CPUStats.SystemUsage < payload.PreCPUStats.SystemUsage {
		return 0
	}

	cpuDelta := float64(payload.CPUStats.CPUUsage.TotalUsage - payload.PreCPUStats.CPUUsage.TotalUsage)
	systemDelta := float64(payload.CPUStats.SystemUsage - payload.PreCPUStats.SystemUsage)
	if cpuDelta <= 0 || systemDelta <= 0 {
		return 0
	}

	onlineCPUs := float64(payload.CPUStats.OnlineCPUs)
	if onlineCPUs == 0 {
		onlineCPUs = float64(len(payload.CPUStats.CPUUsage.PerCPUUsage))
	}
	if onlineCPUs == 0 {
		onlineCPUs = 1
	}

	return (cpuDelta / systemDelta) * onlineCPUs * 100.0
}

func bytesPerSecond(previousBytes, currentBytes uint64, elapsedSeconds float64) float64 {
	if currentBytes < previousBytes || elapsedSeconds <= 0 {
		return 0
	}

	return float64(currentBytes-previousBytes) / elapsedSeconds
}

func (c *DockerCollector) removeStaleNetworkSamples(activeContainerIDs map[string]struct{}) {
	for containerID := range c.previousNetworkStat {
		if _, ok := activeContainerIDs[containerID]; !ok {
			delete(c.previousNetworkStat, containerID)
		}
	}
}

func containerNetworkNames(item container.Summary) []string {
	if item.NetworkSettings == nil || len(item.NetworkSettings.Networks) == 0 {
		return nil
	}

	names := make([]string, 0, len(item.NetworkSettings.Networks))
	for name := range item.NetworkSettings.Networks {
		if strings.TrimSpace(name) != "" {
			names = append(names, name)
		}
	}

	sort.Strings(names)
	return names
}

func buildContainerNetworkIndex(list []container.Summary) map[string]containerNetworkEndpoint {
	index := make(map[string]containerNetworkEndpoint)
	for _, item := range list {
		if item.NetworkSettings == nil {
			continue
		}

		containerID := shortID(item.ID)
		for networkName, endpoint := range item.NetworkSettings.Networks {
			if endpoint == nil {
				continue
			}

			addContainerNetworkEndpoint(index, endpoint.IPAddress, containerID, networkName)
			addContainerNetworkEndpoint(index, endpoint.GlobalIPv6Address, containerID, networkName)
		}
	}

	return index
}

func addContainerNetworkEndpoint(
	index map[string]containerNetworkEndpoint,
	address string,
	containerID string,
	networkName string,
) {
	address = strings.TrimSpace(address)
	if address == "" || containerID == "" {
		return
	}

	index[address] = containerNetworkEndpoint{
		ContainerID: containerID,
		NetworkName: strings.TrimSpace(networkName),
	}
}

func buildDockerNetworkEdges(
	connections []gopsnet.ConnectionStat,
	networkIndex map[string]containerNetworkEndpoint,
) []models.NetworkEdge {
	edgesByKey := make(map[string]models.NetworkEdge)
	for _, connection := range connections {
		localEndpoint, hasLocal := networkIndex[connection.Laddr.IP]
		remoteEndpoint, hasRemote := networkIndex[connection.Raddr.IP]
		if !hasLocal || !hasRemote || localEndpoint.ContainerID == remoteEndpoint.ContainerID {
			continue
		}

		protocol := connectionProtocol(connection.Type)
		key := localEndpoint.ContainerID + "|" + remoteEndpoint.ContainerID + "|" + protocol
		if existing, ok := edgesByKey[key]; ok {
			if existing.State == "" {
				existing.State = strings.ToLower(strings.TrimSpace(connection.Status))
				edgesByKey[key] = existing
			}
			continue
		}

		edgesByKey[key] = models.NetworkEdge{
			SourceID:    localEndpoint.ContainerID,
			TargetID:    remoteEndpoint.ContainerID,
			Protocol:    protocol,
			State:       strings.ToLower(strings.TrimSpace(connection.Status)),
			NetworkName: commonNetworkName(localEndpoint.NetworkName, remoteEndpoint.NetworkName),
		}
	}

	edges := make([]models.NetworkEdge, 0, len(edgesByKey))
	for _, edge := range edgesByKey {
		edges = append(edges, edge)
	}

	sort.Slice(edges, func(i, j int) bool {
		if edges[i].SourceID != edges[j].SourceID {
			return edges[i].SourceID < edges[j].SourceID
		}
		if edges[i].TargetID != edges[j].TargetID {
			return edges[i].TargetID < edges[j].TargetID
		}
		return edges[i].Protocol < edges[j].Protocol
	})
	return edges
}

func parseProcNetworkConnections(text string, containerID string) []procNetworkConnection {
	connections := make([]procNetworkConnection, 0)
	protocol := ""
	ipv6 := false

	for _, rawLine := range strings.Split(text, "\n") {
		line := strings.TrimSpace(rawLine)
		if line == "" {
			continue
		}
		if strings.HasPrefix(line, "INFRA_PROC_NET:") {
			path := strings.TrimPrefix(line, "INFRA_PROC_NET:")
			protocol = ""
			ipv6 = strings.HasSuffix(path, "6")
			switch {
			case strings.Contains(path, "/tcp"):
				protocol = "tcp"
			case strings.Contains(path, "/udp"):
				protocol = "udp"
			}
			continue
		}
		if protocol == "" || strings.HasPrefix(line, "sl ") {
			continue
		}

		fields := strings.Fields(line)
		if len(fields) < 4 {
			continue
		}

		localIP, localPort, ok := parseProcNetworkAddress(fields[1], ipv6)
		if !ok {
			continue
		}
		remoteIP, remotePort, ok := parseProcNetworkAddress(fields[2], ipv6)
		if !ok || remoteIP == "" || remotePort == 0 {
			continue
		}

		state := procNetworkState(protocol, fields[3])
		if state == "listen" {
			continue
		}

		connections = append(connections, procNetworkConnection{
			ContainerID: containerID,
			LocalIP:     localIP,
			LocalPort:   localPort,
			RemoteIP:    remoteIP,
			RemotePort:  remotePort,
			Protocol:    protocol,
			State:       state,
		})
	}

	return connections
}

func parseProcNetworkAddress(value string, ipv6 bool) (string, uint32, bool) {
	parts := strings.Split(value, ":")
	if len(parts) != 2 {
		return "", 0, false
	}

	port, err := strconv.ParseUint(parts[1], 16, 32)
	if err != nil {
		return "", 0, false
	}

	var ip string
	if ipv6 {
		ip = parseProcIPv6(parts[0])
	} else {
		ip = parseProcIPv4(parts[0])
	}
	if ip == "" || ip == "0.0.0.0" || ip == "::" {
		return "", 0, true
	}

	return ip, uint32(port), true
}

func parseProcIPv4(hexAddress string) string {
	if len(hexAddress) != 8 {
		return ""
	}

	bytes := make([]byte, 4)
	for i := 0; i < 4; i++ {
		value, err := strconv.ParseUint(hexAddress[i*2:i*2+2], 16, 8)
		if err != nil {
			return ""
		}
		bytes[3-i] = byte(value)
	}

	return net.IP(bytes).String()
}

func parseProcIPv6(hexAddress string) string {
	if len(hexAddress) != 32 {
		return ""
	}

	bytes := make([]byte, 16)
	for i := 0; i < 4; i++ {
		word := hexAddress[i*8 : i*8+8]
		for j := 0; j < 4; j++ {
			value, err := strconv.ParseUint(word[j*2:j*2+2], 16, 8)
			if err != nil {
				return ""
			}
			bytes[i*4+3-j] = byte(value)
		}
	}

	return net.IP(bytes).String()
}

func procNetworkState(protocol string, hexState string) string {
	if protocol == "udp" {
		return "active"
	}

	switch strings.ToUpper(strings.TrimSpace(hexState)) {
	case "01":
		return "established"
	case "02":
		return "syn_sent"
	case "03":
		return "syn_recv"
	case "04":
		return "fin_wait1"
	case "05":
		return "fin_wait2"
	case "06":
		return "time_wait"
	case "07":
		return "closed"
	case "08":
		return "close_wait"
	case "09":
		return "last_ack"
	case "0A":
		return "listen"
	case "0B":
		return "closing"
	default:
		return strings.ToLower(strings.TrimSpace(hexState))
	}
}

func buildDockerProcNetworkEdges(
	connections []procNetworkConnection,
	networkIndex map[string]containerNetworkEndpoint,
) []models.NetworkEdge {
	edgesByKey := make(map[string]models.NetworkEdge)
	for _, connection := range connections {
		localEndpoint, hasLocal := networkIndex[connection.LocalIP]
		remoteEndpoint, hasRemote := networkIndex[connection.RemoteIP]
		if !hasLocal || !hasRemote || localEndpoint.ContainerID == remoteEndpoint.ContainerID {
			continue
		}

		sourceEndpoint := localEndpoint
		targetEndpoint := remoteEndpoint
		if connection.RemotePort >= ephemeralPortFloor && connection.LocalPort < ephemeralPortFloor {
			sourceEndpoint = remoteEndpoint
			targetEndpoint = localEndpoint
		}

		if sourceEndpoint.ContainerID != connection.ContainerID && targetEndpoint.ContainerID != connection.ContainerID {
			continue
		}

		key := sourceEndpoint.ContainerID + "|" + targetEndpoint.ContainerID + "|" + connection.Protocol
		if existing, ok := edgesByKey[key]; ok {
			if existing.State == "" {
				existing.State = connection.State
				edgesByKey[key] = existing
			}
			continue
		}

		edgesByKey[key] = models.NetworkEdge{
			SourceID:    sourceEndpoint.ContainerID,
			TargetID:    targetEndpoint.ContainerID,
			Protocol:    connection.Protocol,
			State:       connection.State,
			NetworkName: commonNetworkName(sourceEndpoint.NetworkName, targetEndpoint.NetworkName),
		}
	}

	edges := make([]models.NetworkEdge, 0, len(edgesByKey))
	for _, edge := range edgesByKey {
		edges = append(edges, edge)
	}

	sort.Slice(edges, func(i, j int) bool {
		if edges[i].SourceID != edges[j].SourceID {
			return edges[i].SourceID < edges[j].SourceID
		}
		if edges[i].TargetID != edges[j].TargetID {
			return edges[i].TargetID < edges[j].TargetID
		}
		return edges[i].Protocol < edges[j].Protocol
	})
	return edges
}

func connectionProtocol(connectionType uint32) string {
	if connectionType == 2 {
		return "udp"
	}
	return "tcp"
}

func commonNetworkName(left, right string) string {
	if left == right {
		return left
	}
	return ""
}

func containerName(names []string, id string) string {
	for _, name := range names {
		trimmed := strings.TrimPrefix(strings.TrimSpace(name), "/")
		if trimmed != "" {
			return trimmed
		}
	}
	return shortID(id)
}

func shortID(id string) string {
	if len(id) <= 12 {
		return id
	}
	return id[:12]
}

func normalizeContainerStatus(state, statusText string) string {
	state = strings.ToLower(strings.TrimSpace(state))
	if state != "" {
		return state
	}

	statusText = strings.ToLower(strings.TrimSpace(statusText))
	switch {
	case strings.HasPrefix(statusText, "up"):
		return "running"
	case strings.HasPrefix(statusText, "exited"), strings.HasPrefix(statusText, "created"), strings.HasPrefix(statusText, "dead"):
		return "stopped"
	default:
		if statusText == "" {
			return "unknown"
		}
		return statusText
	}
}

func isDockerUnavailableError(err error) bool {
	if err == nil {
		return false
	}

	// Keep agent alive when Docker daemon/socket is unavailable.
	if errors.Is(err, context.DeadlineExceeded) || errors.Is(err, context.Canceled) {
		return true
	}

	lower := strings.ToLower(err.Error())
	return strings.Contains(lower, "cannot connect to the docker daemon") ||
		strings.Contains(lower, "error during connect") ||
		strings.Contains(lower, "connection refused") ||
		strings.Contains(lower, "no such file or directory") ||
		strings.Contains(lower, "the system cannot find the file specified")
}

type dockerStatsPayload struct {
	Read     time.Time `json:"read"`
	CPUStats struct {
		CPUUsage struct {
			TotalUsage  uint64   `json:"total_usage"`
			PerCPUUsage []uint64 `json:"percpu_usage"`
		} `json:"cpu_usage"`
		SystemUsage uint64 `json:"system_cpu_usage"`
		OnlineCPUs  uint32 `json:"online_cpus"`
	} `json:"cpu_stats"`
	PreCPUStats struct {
		CPUUsage struct {
			TotalUsage uint64 `json:"total_usage"`
		} `json:"cpu_usage"`
		SystemUsage uint64 `json:"system_cpu_usage"`
	} `json:"precpu_stats"`
	Networks map[string]struct {
		RXBytes uint64 `json:"rx_bytes"`
		TXBytes uint64 `json:"tx_bytes"`
	} `json:"networks"`
}

type dockerContainerMetrics struct {
	CPU              float64
	RXBytes          uint64
	TXBytes          uint64
	RXBytesPerSecond float64
	TXBytesPerSecond float64
	NetworkNames     []string
}

type containerNetworkSample struct {
	RXBytes uint64
	TXBytes uint64
	ReadAt  time.Time
}

type containerNetworkEndpoint struct {
	ContainerID string
	NetworkName string
}

type procNetworkConnection struct {
	ContainerID string
	LocalIP     string
	LocalPort   uint32
	RemoteIP    string
	RemotePort  uint32
	Protocol    string
	State       string
}
