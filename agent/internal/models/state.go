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
	ID               string   `json:"id"`
	Name             string   `json:"name"`
	Status           string   `json:"status"`
	CPU              float64  `json:"cpu"`
	RXBytes          uint64   `json:"rx_bytes"`
	TXBytes          uint64   `json:"tx_bytes"`
	RXBytesPerSecond float64  `json:"rx_bps"`
	TXBytesPerSecond float64  `json:"tx_bps"`
	NetworkNames     []string `json:"network_names,omitempty"`
}

// NetworkEdge represents an observed Docker-internal network connection.
type NetworkEdge struct {
	SourceID    string  `json:"source_id"`
	TargetID    string  `json:"target_id"`
	Protocol    string  `json:"protocol"`
	State       string  `json:"state,omitempty"`
	NetworkName string  `json:"network_name,omitempty"`
	RXBps       float64 `json:"rx_bps"`
	TXBps       float64 `json:"tx_bps"`
}

// Snapshot is the full state sent to AR clients.
type Snapshot struct {
	Machine      Machine       `json:"machine"`
	Containers   []Container   `json:"containers"`
	NetworkEdges []NetworkEdge `json:"network_edges,omitempty"`
	Timestamp    time.Time     `json:"timestamp"`
}

const connectionMessageType = "connection"

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
	Machine      StreamMachine `json:"machine"`
	Containers   []Container   `json:"containers"`
	NetworkEdges []NetworkEdge `json:"network_edges,omitempty"`
	Timestamp    time.Time     `json:"timestamp"`
}

func NewConnectionStep(machineName string) ConnectionStep {
	return ConnectionStep{
		Type:        connectionMessageType,
		MachineName: machineName,
	}
}

func NewStreamSnapshot(snapshot Snapshot) StreamSnapshot {
	return StreamSnapshot{
		Machine: StreamMachine{
			CPU: snapshot.Machine.CPU,
			RAM: snapshot.Machine.RAM,
		},
		Containers:   snapshot.Containers,
		NetworkEdges: snapshot.NetworkEdges,
		Timestamp:    snapshot.Timestamp,
	}
}

func ToStreamSnapshot(snapshot Snapshot) StreamSnapshot {
	return NewStreamSnapshot(snapshot)
}
