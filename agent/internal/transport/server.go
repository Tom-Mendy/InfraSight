package transport

import (
	"context"
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
)

const (
	defaultHost     = "0.0.0.0"
	defaultPort     = 8080
	shutdownTimeout = 5 * time.Second
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
	mux.HandleFunc("/scan", s.handleScan)
	mux.HandleFunc("/ws", s.handleWebSocket)
}
