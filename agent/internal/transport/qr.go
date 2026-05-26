package transport

import (
	"encoding/json"
	"fmt"

	"github.com/skip2/go-qrcode"
)

func (s *Server) QRPayloadJSON() (string, error) {
	raw, err := json.Marshal(s.qrPayload)
	if err != nil {
		return "", fmt.Errorf("marshal qr payload: %w", err)
	}
	return string(raw), nil
}

func (s *Server) QRCodeASCII() (string, error) {
	content, err := s.QRPayloadJSON()
	if err != nil {
		return "", err
	}
	code, err := qrcode.New(content, qrcode.Medium)
	if err != nil {
		return "", fmt.Errorf("build qr code: %w", err)
	}
	return code.ToSmallString(false), nil
}

func (s *Server) QRCodePayloadJSON() (string, error) {
	qrPayload := s.qrPayload.ToQRCodePayload()
	raw, err := json.Marshal(qrPayload)
	if err != nil {
		return "", fmt.Errorf("marshal qr code payload: %w", err)
	}
	return string(raw), nil
}

func (s *Server) QRPng() ([]byte, error) {
	content, err := s.QRCodePayloadJSON()
	if err != nil {
		return nil, err
	}
	code, err := qrcode.New(content, qrcode.Medium)
	if err != nil {
		return nil, fmt.Errorf("build qr code: %w", err)
	}

	pngData, err := code.PNG(256)
	if err != nil {
		return nil, fmt.Errorf("encode qr code to png: %w", err)
	}

	return pngData, nil
}
