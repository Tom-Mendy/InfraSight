package collector

import (
	"context"

	"infrasight-agent/internal/models"
)

type MachineCollector interface {
	CollectMachine(ctx context.Context) (models.Machine, error)
}

type ContainerCollector interface {
	CollectContainers(ctx context.Context) ([]models.Container, error)
}
