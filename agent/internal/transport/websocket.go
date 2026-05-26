package transport

import (
	"net/http"
	"strings"
	"time"

	"infrasight-agent/internal/models"

	"github.com/gorilla/websocket"
)

const (
	websocketWriteWait = 5 * time.Second
	websocketPongWait  = 60 * time.Second
	websocketPingEvery = 25 * time.Second
	websocketReadLimit = 8 * 1024
)

func (s *Server) handleWebSocket(w http.ResponseWriter, r *http.Request) {
	clientAddress := websocketClientAddress(r)
	s.logger.Info("websocket connection attempt", "client", clientAddress, "machine", s.qrPayload.Name)

	conn, err := s.upgrader.Upgrade(w, r, nil)
	if err != nil {
		s.logger.Warn("websocket upgrade failed", "client", clientAddress, "error", err)
		return
	}
	defer conn.Close()

	if err := writeWebSocketJSON(conn, models.NewConnectionStep(s.qrPayload.Name)); err != nil {
		s.logger.Debug("websocket write failed sending machine name", "client", clientAddress, "error", err)
		return
	}

	s.logger.Info("websocket client connected", "client", clientAddress, "machine", s.qrPayload.Name)
	defer s.logger.Info("websocket client disconnected", "client", clientAddress, "machine", s.qrPayload.Name)

	subscriberID, updates := s.state.Subscribe(1)
	defer s.state.Unsubscribe(subscriberID)

	done := make(chan struct{})
	go s.readLoop(conn, done)

	pingTicker := time.NewTicker(websocketPingEvery)
	defer pingTicker.Stop()

	for {
		select {
		case <-r.Context().Done():
			return
		case <-done:
			return
		case snapshot, ok := <-updates:
			if !ok {
				return
			}
			if err := writeWebSocketJSON(conn, models.NewStreamSnapshot(snapshot)); err != nil {
				s.logger.Debug("websocket write failed", "error", err)
				return
			}
		case <-pingTicker.C:
			if err := writeWebSocketPing(conn); err != nil {
				s.logger.Debug("websocket ping failed", "error", err)
				return
			}
		}
	}
}

func (s *Server) readLoop(conn *websocket.Conn, done chan<- struct{}) {
	defer close(done)

	conn.SetReadLimit(websocketReadLimit)
	conn.SetReadDeadline(time.Now().Add(websocketPongWait))
	conn.SetPongHandler(func(string) error {
		conn.SetReadDeadline(time.Now().Add(websocketPongWait))
		return nil
	})

	for {
		_, payload, err := conn.ReadMessage()
		if err != nil {
			return
		}
		if len(payload) == 0 {
			continue
		}
		s.logger.Debug("received client websocket message", "payload", string(payload))
	}
}

func writeWebSocketJSON(conn *websocket.Conn, value any) error {
	conn.SetWriteDeadline(time.Now().Add(websocketWriteWait))
	return conn.WriteJSON(value)
}

func writeWebSocketPing(conn *websocket.Conn) error {
	conn.SetWriteDeadline(time.Now().Add(websocketWriteWait))
	return conn.WriteControl(websocket.PingMessage, nil, time.Now().Add(websocketWriteWait))
}

func websocketClientAddress(r *http.Request) string {
	if forwardedFor := strings.TrimSpace(r.Header.Get("X-Forwarded-For")); forwardedFor != "" {
		parts := strings.Split(forwardedFor, ",")
		if len(parts) > 0 {
			return strings.TrimSpace(parts[0])
		}
	}

	if realIP := strings.TrimSpace(r.Header.Get("X-Real-IP")); realIP != "" {
		return realIP
	}

	return r.RemoteAddr
}
