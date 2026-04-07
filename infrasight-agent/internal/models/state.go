package models

import "time"

// Machine represents host-level metrics.
type Machine struct {
	Name string  `json:"name"`
	CPU  float64 `json:"cpu"`
	RAM  float64 `json:"ram"`
}

// Container represents a Docker container state.
type Container struct {
	ID     string  `json:"id"`
	Name   string  `json:"name"`
	Status string  `json:"status"`
	CPU    float64 `json:"cpu"`
}

// Snapshot is the full state sent to AR clients.
type Snapshot struct {
	Machine    Machine     `json:"machine"`
	Containers []Container `json:"containers"`
	Timestamp  time.Time   `json:"timestamp"`
}

// ConnectionStep is sent once as the first WebSocket message.
type ConnectionStep struct {
	Type        string `json:"type"`
	MachineName string `json:"machine_name"`
}

// StreamMachine omits static metadata and only sends changing metrics.
type StreamMachine struct {
	CPU float64 `json:"cpu"`
	RAM float64 `json:"ram"`
}

// StreamSnapshot is the recurring message sent over WebSocket after the initial handshake.
type StreamSnapshot struct {
	Machine    StreamMachine `json:"machine"`
	Containers []Container   `json:"containers"`
	Timestamp  time.Time     `json:"timestamp"`
}

func ToStreamSnapshot(snapshot Snapshot) StreamSnapshot {
	return StreamSnapshot{
		Machine: StreamMachine{
			CPU: snapshot.Machine.CPU,
			RAM: snapshot.Machine.RAM,
		},
		Containers: snapshot.Containers,
		Timestamp:  snapshot.Timestamp,
	}
}
