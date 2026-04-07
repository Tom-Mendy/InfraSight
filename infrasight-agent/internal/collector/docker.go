package collector

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"sort"
	"strings"
	"time"

	"infrasight-agent/internal/models"

	"github.com/docker/docker/api/types/container"
	"github.com/docker/docker/client"
)

const (
	dockerListTimeout  = 2 * time.Second
	dockerStatsTimeout = 1500 * time.Millisecond
)

type noopDockerCollector struct{}

func (noopDockerCollector) CollectContainers(context.Context) ([]models.Container, error) {
	return []models.Container{}, nil
}

type DockerCollector struct {
	client *client.Client
}

func NewDockerCollector(enabled bool) (ContainerCollector, error) {
	if !enabled {
		return noopDockerCollector{}, nil
	}

	cli, err := client.NewClientWithOpts(client.FromEnv, client.WithAPIVersionNegotiation())
	if err != nil {
		return nil, fmt.Errorf("create docker client: %w", err)
	}

	return &DockerCollector{client: cli}, nil
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
	for _, item := range list {
		status := normalizeContainerStatus(item.State, item.Status)
		cpuUsage := 0.0

		if status == "running" {
			statsCtx, statsCancel := context.WithTimeout(parent, dockerStatsTimeout)
			cpu, statsErr := c.collectContainerCPU(statsCtx, item.ID)
			statsCancel()
			if statsErr == nil {
				cpuUsage = cpu
			}
		}

		out = append(out, models.Container{
			ID:     shortID(item.ID),
			Name:   containerName(item.Names, item.ID),
			Status: status,
			CPU:    cpuUsage,
		})
	}

	sort.Slice(out, func(i, j int) bool {
		return out[i].Name < out[j].Name
	})
	return out, nil
}

func (c *DockerCollector) collectContainerCPU(ctx context.Context, containerID string) (float64, error) {
	stats, err := c.client.ContainerStats(ctx, containerID, false)
	if err != nil {
		return 0, err
	}
	defer stats.Body.Close()

	var payload dockerStatsPayload
	if err := json.NewDecoder(stats.Body).Decode(&payload); err != nil {
		return 0, err
	}

	cpuDelta := float64(payload.CPUStats.CPUUsage.TotalUsage - payload.PreCPUStats.CPUUsage.TotalUsage)
	systemDelta := float64(payload.CPUStats.SystemUsage - payload.PreCPUStats.SystemUsage)
	if cpuDelta <= 0 || systemDelta <= 0 {
		return 0, nil
	}

	onlineCPUs := float64(payload.CPUStats.OnlineCPUs)
	if onlineCPUs == 0 {
		onlineCPUs = float64(len(payload.CPUStats.CPUUsage.PerCPUUsage))
	}
	if onlineCPUs == 0 {
		onlineCPUs = 1
	}

	return (cpuDelta / systemDelta) * onlineCPUs * 100.0, nil
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
}
