package models

import "fmt"

// QRPayload is encoded in the onboarding QR code.
type QRPayload struct {
	Name string `json:"name"`
	IP   string `json:"ip"`
	Port int    `json:"port"`
}

func (q QRPayload) WebSocketURL() string {
	return fmt.Sprintf("ws://%s:%d/ws", q.IP, q.Port)
}
