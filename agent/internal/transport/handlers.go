package transport

import (
	"net/http"
	"time"
)

func (s *Server) handleIndex(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"name":      "InfraSight Agent",
		"host":      s.host,
		"port":      s.port,
		"websocket": s.qrPayload.WebSocketURL(),
		"endpoints": []string{"/ws", "/state", "/health", "/qr", "/qr.png"},
	})
}

func (s *Server) handleHealth(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"status":           "ok",
		"time":             time.Now().UTC(),
		"machine":          s.qrPayload.Name,
		"connections":      s.state.SubscriberCount(),
		"websocket_target": s.qrPayload.WebSocketURL(),
	})
}

func (s *Server) handleState(w http.ResponseWriter, _ *http.Request) {
	snapshot, ok := s.state.GetLatest()
	if !ok {
		writeJSON(w, http.StatusServiceUnavailable, map[string]string{
			"error": "state not ready yet",
		})
		return
	}
	writeJSON(w, http.StatusOK, snapshot)
}

func (s *Server) handleQRPayload(w http.ResponseWriter, _ *http.Request) {
	qrPayload := s.qrPayload.ToQRCodePayload()
	writeJSON(w, http.StatusOK, map[string]any{
		"ip":   qrPayload.IP,
		"port": qrPayload.Port,
		"ws":   s.qrPayload.WebSocketURL(),
	})
}

func (s *Server) handleQRPng(w http.ResponseWriter, _ *http.Request) {
	png, err := s.QRPngInverted()
	if err != nil {
		s.logger.Error("failed to generate inverted qr image", "error", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{
			"error": "failed to generate qr image",
		})
		return
	}

	w.Header().Set("Content-Type", "image/png")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(png)
}
