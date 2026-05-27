package transport

import (
	"bytes"
	"image/png"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
)

func TestHandleScanServesSpatialQrPage(t *testing.T) {
	recorder := httptest.NewRecorder()
	(&Server{}).handleScan(recorder, httptest.NewRequest(http.MethodGet, "/scan", nil))

	response := recorder.Result()
	if response.StatusCode != http.StatusOK {
		t.Fatalf("unexpected status: got %d want %d", response.StatusCode, http.StatusOK)
	}
	if got := response.Header.Get("Content-Type"); got != "text/html; charset=utf-8" {
		t.Fatalf("unexpected content type: %q", got)
	}

	body := recorder.Body.String()
	if !strings.Contains(body, `src="/qr.png"`) || !strings.Contains(body, "placement spatial") {
		t.Fatalf("scan page does not contain its official spatial QR instructions: %s", body)
	}
}

func TestQRPngUsesStandardLightBackground(t *testing.T) {
	pngData, err := (&Server{}).QRPng()
	if err != nil {
		t.Fatalf("generate qr png: %v", err)
	}

	image, err := png.Decode(bytes.NewReader(pngData))
	if err != nil {
		t.Fatalf("decode qr png: %v", err)
	}

	r, g, b, _ := image.At(image.Bounds().Min.X, image.Bounds().Min.Y).RGBA()
	if r < 0xf000 || g < 0xf000 || b < 0xf000 {
		t.Fatalf("corner pixel is not the standard light QR background: r=%#x g=%#x b=%#x", r, g, b)
	}
}
