package collector

import (
	"context"
	"encoding/json"
	"fmt"
	"os/exec"
	"sort"
	"strconv"
	"strings"
	"time"

	"infrasight-agent/internal/models"
)

const (
	dockerCommandTimeout = 2 * time.Second
)

type noopDockerCollector struct{}

func (noopDockerCollector) CollectContainers(context.Context) ([]models.Container, error) {
	return []models.Container{}, nil
}

type DockerCollector struct {
	dockerPath string
}

func NewDockerCollector(enabled bool) (ContainerCollector, error) {
	if !enabled {
		return noopDockerCollector{}, nil
	}

	dockerPath, err := exec.LookPath("docker")
	if err != nil {
		return nil, fmt.Errorf("docker cli not found: %w", err)
	}

	return &DockerCollector{dockerPath: dockerPath}, nil
}

func (c *DockerCollector) CollectContainers(ctx context.Context) ([]models.Container, error) {
	containers, err := c.listContainers(ctx)
	if err != nil {
		return nil, err
	}

	cpuByContainer := c.readRunningContainerCPU(ctx)
	for i := range containers {
		if cpu, ok := cpuByContainer[containers[i].ID]; ok {
			containers[i].CPU = cpu
		}
	}

	sort.Slice(containers, func(i, j int) bool {
		return containers[i].Name < containers[j].Name
	})
	return containers, nil
}

func (c *DockerCollector) listContainers(parent context.Context) ([]models.Container, error) {
	ctx, cancel := context.WithTimeout(parent, dockerCommandTimeout)
	defer cancel()

	output, err := exec.CommandContext(ctx, c.dockerPath, "ps", "-a", "--format", "{{json .}}").Output()
	if err != nil {
		// Docker might be installed but daemon not running. In that case keep the agent alive.
		return []models.Container{}, nil
	}

	lines := splitNonEmptyLines(string(output))
	containers := make([]models.Container, 0, len(lines))
	for _, line := range lines {
		row, err := parseDockerPSLine(line)
		if err != nil {
			continue
		}

		containers = append(containers, models.Container{
			ID:     shortID(row.ID),
			Name:   row.Name,
			Status: normalizeStatus(row.State),
			CPU:    0,
		})
	}

	return containers, nil
}

func (c *DockerCollector) readRunningContainerCPU(parent context.Context) map[string]float64 {
	ctx, cancel := context.WithTimeout(parent, dockerCommandTimeout)
	defer cancel()

	output, err := exec.CommandContext(ctx, c.dockerPath, "stats", "--no-stream", "--format", "{{json .}}").Output()
	if err != nil {
		return map[string]float64{}
	}

	result := make(map[string]float64)
	for _, line := range splitNonEmptyLines(string(output)) {
		row, err := parseDockerStatsLine(line)
		if err != nil {
			continue
		}
		result[shortID(row.ID)] = parsePercent(row.CPUPercent)
	}
	return result
}

func splitNonEmptyLines(raw string) []string {
	parts := strings.Split(raw, "\n")
	out := make([]string, 0, len(parts))
	for _, part := range parts {
		trimmed := strings.TrimSpace(part)
		if trimmed != "" {
			out = append(out, trimmed)
		}
	}
	return out
}

func parseDockerPSLine(line string) (dockerPSLine, error) {
	return parseJSONObjectLine[dockerPSLine](line)
}

func parseDockerStatsLine(line string) (dockerStatsLine, error) {
	return parseJSONObjectLine[dockerStatsLine](line)
}

func parseJSONObjectLine[T any](line string) (T, error) {
	var out T
	if err := jsonUnmarshal(line, &out); err != nil {
		return out, err
	}
	return out, nil
}

func jsonUnmarshal(raw string, out any) error {
	decoder := strings.NewReader(raw)
	return json.NewDecoder(decoder).Decode(out)
}

func normalizeStatus(state string) string {
	state = strings.ToLower(strings.TrimSpace(state))
	if state == "" {
		return "unknown"
	}
	return state
}

func parsePercent(raw string) float64 {
	raw = strings.TrimSpace(strings.TrimSuffix(raw, "%"))
	if raw == "" {
		return 0
	}
	value, err := strconv.ParseFloat(raw, 64)
	if err != nil {
		return 0
	}
	return value
}

func shortID(id string) string {
	if len(id) <= 12 {
		return id
	}
	return id[:12]
}

type dockerPSLine struct {
	ID    string `json:"ID"`
	Name  string `json:"Names"`
	State string `json:"State"`
}

type dockerStatsLine struct {
	ID         string `json:"ID"`
	CPUPercent string `json:"CPUPerc"`
}
