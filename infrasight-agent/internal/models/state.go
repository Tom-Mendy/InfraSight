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

// ConnectionStep is sent as the first message when a client connects
type ConnectionStep struct {
	Type   string `json:"type"`    // "connection" or "qr_code"
	QRCode string `json:"qr_code"` // base64 encoded PNG image
}
