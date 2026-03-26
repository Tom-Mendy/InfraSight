package collector

import (
	"context"
	"fmt"

	"infrasight-agent/internal/models"

	"github.com/shirou/gopsutil/v4/cpu"
	"github.com/shirou/gopsutil/v4/mem"
)

type SystemCollector struct {
	machineName string
}

func NewSystemCollector(machineName string) *SystemCollector {
	return &SystemCollector{machineName: machineName}
}

func (c *SystemCollector) CollectMachine(ctx context.Context) (models.Machine, error) {
	cpuUsage, err := cpu.PercentWithContext(ctx, 0, false)
	if err != nil {
		return models.Machine{}, fmt.Errorf("collect cpu usage: %w", err)
	}
	if len(cpuUsage) == 0 {
		return models.Machine{}, fmt.Errorf("collect cpu usage: empty result")
	}

	vm, err := mem.VirtualMemoryWithContext(ctx)
	if err != nil {
		return models.Machine{}, fmt.Errorf("collect memory usage: %w", err)
	}

	return models.Machine{
		Name: c.machineName,
		CPU:  cpuUsage[0],
		RAM:  vm.UsedPercent,
	}, nil
}
