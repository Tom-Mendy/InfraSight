package transport

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net"
	"net/http"
	"strconv"
	"strings"
	"time"

	"infrasight-agent/internal/models"
	"infrasight-agent/internal/service"

	"github.com/gorilla/websocket"
	"github.com/skip2/go-qrcode"
)

const (
	defaultHost        = "0.0.0.0"
	defaultPort        = 8080
	shutdownTimeout    = 5 * time.Second
	websocketWriteWait = 5 * time.Second
	websocketPongWait  = 60 * time.Second
	websocketPingEvery = 25 * time.Second
	websocketReadLimit = 8 * 1024
)

type ServerOptions struct {
	Host        string
	Port        int
	MachineName string
	AdvertiseIP string
	State       *service.StateService
	Logger      *slog.Logger
}

type Server struct {
	logger    *slog.Logger
	host      string
	port      int
	addr      string
	state     *service.StateService
	qrPayload models.QRPayload
	upgrader  websocket.Upgrader
}

func NewServer(opts ServerOptions) (*Server, error) {
	if opts.State == nil {
		return nil, fmt.Errorf("state service is required")
	}
	if opts.Logger == nil {
		opts.Logger = slog.Default()
	}
	if opts.Host == "" {
		opts.Host = defaultHost
	}
	if opts.Port <= 0 {
		opts.Port = defaultPort
	}
	if strings.TrimSpace(opts.MachineName) == "" {
		opts.MachineName = "infrasight-agent"
	}

	advertiseIP, err := resolveAdvertiseIP(opts.Host, opts.AdvertiseIP)
	if err != nil {
		return nil, fmt.Errorf("resolve advertise ip: %w", err)
	}

	return &Server{
		logger: opts.Logger,
		host:   opts.Host,
		port:   opts.Port,
		addr:   net.JoinHostPort(opts.Host, strconv.Itoa(opts.Port)),
		state:  opts.State,
		qrPayload: models.QRPayload{
			Name: opts.MachineName,
			IP:   advertiseIP,
			Port: opts.Port,
		},
		upgrader: websocket.Upgrader{
			ReadBufferSize:  1024,
			WriteBufferSize: 1024,
			CheckOrigin: func(*http.Request) bool {
				return true
			},
		},
	}, nil
}

func (s *Server) Addr() string {
	return s.addr
}

func (s *Server) QRPayload() models.QRPayload {
	return s.qrPayload
}

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

func (s *Server) Run(ctx context.Context) error {
	mux := http.NewServeMux()
	s.registerRoutes(mux)

	server := &http.Server{
		Addr:              s.addr,
		Handler:           mux,
		ReadHeaderTimeout: 5 * time.Second,
	}

	go func() {
		<-ctx.Done()
		shutdownCtx, cancel := context.WithTimeout(context.Background(), shutdownTimeout)
		defer cancel()
		_ = server.Shutdown(shutdownCtx)
	}()

	err := server.ListenAndServe()
	if err != nil && !errors.Is(err, http.ErrServerClosed) {
		return err
	}
	return nil
}

func (s *Server) registerRoutes(mux *http.ServeMux) {
	mux.HandleFunc("/", s.handleIndex)
	mux.HandleFunc("/health", s.handleHealth)
	mux.HandleFunc("/state", s.handleState)
	mux.HandleFunc("/qr", s.handleQRPayload)
	mux.HandleFunc("/qr.png", s.handleQRPng)
	mux.HandleFunc("/ws", s.handleWebSocket)
}

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
		"status":          "ok",
		"time":            time.Now().UTC(),
		"machine":         s.qrPayload.Name,
		"connections":     s.state.SubscriberCount(),
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
	writeJSON(w, http.StatusOK, map[string]any{
		"name": s.qrPayload.Name,
		"ip":   s.qrPayload.IP,
		"port": s.qrPayload.Port,
		"ws":   s.qrPayload.WebSocketURL(),
	})
}

func (s *Server) handleQRPng(w http.ResponseWriter, _ *http.Request) {
	content, err := s.QRPayloadJSON()
	if err != nil {
		s.logger.Error("failed to encode qr payload", "error", err)
		writeJSON(w, http.StatusInternalServerError, map[string]string{
			"error": "failed to generate qr payload",
		})
		return
	}

	png, err := qrcode.Encode(content, qrcode.Medium, 256)
	if err != nil {
		s.logger.Error("failed to render qr image", "error", err)
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

func (s *Server) handleWebSocket(w http.ResponseWriter, r *http.Request) {
	conn, err := s.upgrader.Upgrade(w, r, nil)
	if err != nil {
		s.logger.Warn("websocket upgrade failed", "error", err)
		return
	}
	defer conn.Close()

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
			if err := writeWebSocketJSON(conn, snapshot); err != nil {
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

func writeJSON(w http.ResponseWriter, status int, value any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(value)
}

func resolveAdvertiseIP(bindHost, explicitAdvertiseIP string) (string, error) {
	explicitAdvertiseIP = strings.TrimSpace(explicitAdvertiseIP)
	if explicitAdvertiseIP != "" {
		return explicitAdvertiseIP, nil
	}

	normalizedHost := strings.TrimSpace(strings.ToLower(bindHost))
	if normalizedHost != "" &&
		normalizedHost != "0.0.0.0" &&
		normalizedHost != "::" &&
		normalizedHost != "localhost" {
		return bindHost, nil
	}

	ip, err := detectLocalIPv4()
	if err != nil {
		return "", err
	}
	return ip, nil
}

func detectLocalIPv4() (string, error) {
	interfaces, err := net.Interfaces()
	if err != nil {
		return "", fmt.Errorf("list network interfaces: %w", err)
	}

	firstCandidate := ""
	for _, iface := range interfaces {
		if iface.Flags&net.FlagUp == 0 || iface.Flags&net.FlagLoopback != 0 {
			continue
		}

		addrs, err := iface.Addrs()
		if err != nil {
			continue
		}

		for _, addr := range addrs {
			ip := addressToIP(addr)
			if ip == nil || ip.IsLoopback() {
				continue
			}
			v4 := ip.To4()
			if v4 == nil {
				continue
			}

			if isPrivateIPv4(v4) {
				return v4.String(), nil
			}

			if firstCandidate == "" {
				firstCandidate = v4.String()
			}
		}
	}

	if firstCandidate != "" {
		return firstCandidate, nil
	}

	conn, err := net.Dial("udp", "8.8.8.8:80")
	if err == nil {
		defer conn.Close()
		if udpAddr, ok := conn.LocalAddr().(*net.UDPAddr); ok && udpAddr.IP != nil {
			if v4 := udpAddr.IP.To4(); v4 != nil {
				return v4.String(), nil
			}
		}
	}

	return "", fmt.Errorf("no non-loopback IPv4 address found")
}

func addressToIP(addr net.Addr) net.IP {
	switch v := addr.(type) {
	case *net.IPNet:
		return v.IP
	case *net.IPAddr:
		return v.IP
	default:
		return nil
	}
}

func isPrivateIPv4(ip net.IP) bool {
	if len(ip) != net.IPv4len {
		return false
	}

	switch {
	case ip[0] == 10:
		return true
	case ip[0] == 172 && ip[1] >= 16 && ip[1] <= 31:
		return true
	case ip[0] == 192 && ip[1] == 168:
		return true
	default:
		return false
	}
}
