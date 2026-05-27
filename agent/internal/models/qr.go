package models

import "fmt"

// QRPayload is encoded in the onboarding QR code.
type QRPayload struct {
	Name string `json:"name"`
	IP   string `json:"ip"`
	Port int    `json:"port"`
}

// QRCodePayload excludes the Name field for encoding in QR code
type QRCodePayload struct {
	IP   string `json:"ip"`
	Port int    `json:"port"`
}

func (q QRPayload) WebSocketURL() string {
	return fmt.Sprintf("ws://%s:%d/ws", q.IP, q.Port)
}

// ToQRCodePayload returns the payload to be encoded in the QR code (without Name)
func (q QRPayload) ToQRCodePayload() QRCodePayload {
	return QRCodePayload{
		IP:   q.IP,
		Port: q.Port,
	}
}
