package service

import (
	"context"
	"log/slog"
	"sync"
	"time"

	"infrasight-agent/internal/collector"
	"infrasight-agent/internal/models"
)

const (
	defaultUpdateInterval = time.Second
	collectionTimeout     = 3 * time.Second
)

type StateService struct {
	logger             *slog.Logger
	machineCollector   collector.MachineCollector
	containerCollector collector.ContainerCollector
	interval           time.Duration

	mu                sync.RWMutex
	latest            models.Snapshot
	hasSnapshot       bool
	subscribers       map[int]chan models.Snapshot
	nextSubscriberID  int
}

func NewStateService(
	logger *slog.Logger,
	machineCollector collector.MachineCollector,
	containerCollector collector.ContainerCollector,
	interval time.Duration,
) *StateService {
	if logger == nil {
		logger = slog.Default()
	}
	if interval <= 0 {
		interval = defaultUpdateInterval
	}

	return &StateService{
		logger:             logger,
		machineCollector:   machineCollector,
		containerCollector: containerCollector,
		interval:           interval,
		subscribers:        make(map[int]chan models.Snapshot),
	}
}

func (s *StateService) Run(ctx context.Context) {
	s.collectAndBroadcast(ctx)

	ticker := time.NewTicker(s.interval)
	defer ticker.Stop()
	defer s.closeSubscribers()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			s.collectAndBroadcast(ctx)
		}
	}
}

func (s *StateService) GetLatest() (models.Snapshot, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.latest, s.hasSnapshot
}

func (s *StateService) SubscriberCount() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.subscribers)
}

func (s *StateService) Subscribe(buffer int) (int, <-chan models.Snapshot) {
	if buffer <= 0 {
		buffer = 1
	}

	s.mu.Lock()
	defer s.mu.Unlock()

	s.nextSubscriberID++
	id := s.nextSubscriberID
	ch := make(chan models.Snapshot, buffer)
	s.subscribers[id] = ch

	if s.hasSnapshot {
		ch <- s.latest
	}

	return id, ch
}

func (s *StateService) Unsubscribe(id int) {
	s.mu.Lock()
	defer s.mu.Unlock()

	ch, ok := s.subscribers[id]
	if !ok {
		return
	}

	delete(s.subscribers, id)
	close(ch)
}

func (s *StateService) collectAndBroadcast(parent context.Context) {
	ctx, cancel := context.WithTimeout(parent, collectionTimeout)
	defer cancel()

	machine, err := s.machineCollector.CollectMachine(ctx)
	if err != nil {
		s.logger.Warn("failed to collect machine metrics", "error", err)
		return
	}

	containers, err := s.containerCollector.CollectContainers(ctx)
	if err != nil {
		s.logger.Warn("failed to collect docker metrics", "error", err)
		containers = []models.Container{}
	}

	snapshot := models.Snapshot{
		Machine:    machine,
		Containers: containers,
		Timestamp:  time.Now().UTC(),
	}

	s.mu.Lock()
	s.latest = snapshot
	s.hasSnapshot = true
	for _, ch := range s.subscribers {
		select {
		case ch <- snapshot:
		default:
			select {
			case <-ch:
			default:
			}
			select {
			case ch <- snapshot:
			default:
			}
		}
	}
	s.mu.Unlock()
}

func (s *StateService) closeSubscribers() {
	s.mu.Lock()
	defer s.mu.Unlock()

	for id, ch := range s.subscribers {
		close(ch)
		delete(s.subscribers, id)
	}
}
