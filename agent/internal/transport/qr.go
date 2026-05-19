package transport

import (
	"bytes"
	"encoding/json"
	"fmt"
	"image"
	"image/color"
	"image/png"

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

func (s *Server) QRPngInverted() ([]byte, error) {
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

	inverted, err := invertImage(pngData)
	if err != nil {
		return nil, fmt.Errorf("invert qr code colors: %w", err)
	}

	return inverted, nil
}

func invertImage(pngData []byte) ([]byte, error) {
	img, err := png.Decode(bytes.NewReader(pngData))
	if err != nil {
		return nil, fmt.Errorf("decode png: %w", err)
	}

	bounds := img.Bounds()
	inverted := image.NewRGBA(bounds)

	for y := bounds.Min.Y; y < bounds.Max.Y; y++ {
		for x := bounds.Min.X; x < bounds.Max.X; x++ {
			r, g, b, a := img.At(x, y).RGBA()
			inverted.SetRGBA(x, y, color.RGBA{
				R: 255 - uint8(r>>8),
				G: 255 - uint8(g>>8),
				B: 255 - uint8(b>>8),
				A: uint8(a >> 8),
			})
		}
	}

	buf := new(bytes.Buffer)
	if err := png.Encode(buf, inverted); err != nil {
		return nil, fmt.Errorf("encode inverted png: %w", err)
	}

	return buf.Bytes(), nil
}
