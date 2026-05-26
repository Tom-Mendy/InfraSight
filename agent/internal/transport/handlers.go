package transport

import (
	"io"
	"net/http"
	"time"
)

func (s *Server) handleIndex(w http.ResponseWriter, _ *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"name":      "InfraSight Agent",
		"host":      s.host,
		"port":      s.port,
		"websocket": s.qrPayload.WebSocketURL(),
		"endpoints": []string{"/ws", "/state", "/health", "/qr", "/qr.png", "/scan"},
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
	png, err := s.QRPng()
	if err != nil {
		s.logger.Error("failed to generate qr image", "error", err)
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

func (s *Server) handleScan(w http.ResponseWriter, _ *http.Request) {
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(http.StatusOK)
	_, _ = io.WriteString(w, `<!doctype html>
<html lang="fr">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>InfraSight - QR spatial</title>
  <style>
    :root { color-scheme: dark; font-family: system-ui, sans-serif; }
    body { align-items: center; background: #101621; display: flex; flex-direction: column; justify-content: center; margin: 0; min-height: 100vh; text-align: center; }
    main { max-width: min(92vw, 560px); }
    img { background: #fff; border-radius: 12px; display: block; height: auto; margin: 1.25rem auto; max-width: min(80vw, 440px); image-rendering: pixelated; width: 100%; }
    p { color: #b6c1d2; line-height: 1.45; }
  </style>
</head>
<body>
  <main>
    <h1>Scanner cette machine</h1>
    <img src="/qr.png" alt="QR code InfraSight de connexion">
    <p>Ce QR sert au placement spatial Android. Le zoom de la page est autorise.</p>
  </main>
</body>
</html>`)
}
