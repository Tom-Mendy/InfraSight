package main

import (
	"context"
	"fmt"
	"io"
	"log/slog"
	"os"
	"os/signal"
	"syscall"

	"infrasight-agent/internal/collector"
	"infrasight-agent/internal/config"
	"infrasight-agent/internal/service"
	"infrasight-agent/internal/transport"
)

func main() {
	fmt.Println("Starting infrasight agent...")
	if err := run(); err != nil {
		fmt.Fprintf(os.Stderr, "infrasight-agent error: %v\n", err)
		os.Exit(1)
	}
}

func run() error {
	logger := slog.New(slog.NewTextHandler(os.Stdout, &slog.HandlerOptions{Level: slog.LevelInfo}))
	cfg, err := config.Load()
	if err != nil {
		return err
	}

	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	machineCollector := collector.NewSystemCollector(cfg.MachineName)
	containerCollector, dockerCloser := createContainerCollector(cfg.DockerEnabled, logger)
	if dockerCloser != nil {
		defer dockerCloser.Close()
	}

	stateService := service.NewStateService(
		logger,
		machineCollector,
		containerCollector,
		cfg.UpdateInterval,
	)
	go stateService.Run(ctx)

	server, err := transport.NewServer(transport.ServerOptions{
		Host:        cfg.Host,
		Port:        cfg.Port,
		MachineName: cfg.MachineName,
		AdvertiseIP: cfg.AdvertiseIP,
		State:       stateService,
		Logger:      logger,
	})
	if err != nil {
		return fmt.Errorf("create transport server: %w", err)
	}

	logStartup(logger, server)
	if err := server.Run(ctx); err != nil {
		return fmt.Errorf("run transport server: %w", err)
	}

	logger.Info("infrasight agent stopped")
	return nil
}

func createContainerCollector(enabled bool, logger *slog.Logger) (collector.ContainerCollector, io.Closer) {
	containerCollector, err := collector.NewDockerCollector(enabled)
	if err != nil {
		logger.Warn("docker collector unavailable, continuing without docker metrics", "error", err)
		containerCollector, _ = collector.NewDockerCollector(false)
	}

	var closer io.Closer
	if c, ok := containerCollector.(interface{ Close() error }); ok {
		closer = c
	}

	return containerCollector, closer
}

func logStartup(logger *slog.Logger, server *transport.Server) {
	payload := server.QRPayload()
	advertiseAddr := fmt.Sprintf("%s:%d", payload.IP, payload.Port)

	logger.Info("infrasight agent ready",
		"http_addr", server.Addr(),
		"machine", payload.Name,
		"ws_url", payload.WebSocketURL(),
		"health_url", fmt.Sprintf("http://%s/health", advertiseAddr),
		"state_url", fmt.Sprintf("http://%s/state", advertiseAddr),
		"qr_payload_url", fmt.Sprintf("http://%s/qr", advertiseAddr),
		"qr_image_url", fmt.Sprintf("http://%s/qr.png", advertiseAddr),
		"qr_scan_url", fmt.Sprintf("http://%s/scan", advertiseAddr),
	)

	payloadJSON, err := server.QRPayloadJSON()
	if err != nil {
		logger.Warn("failed to render qr payload json", "error", err)
		return
	}
	logger.Info("qr payload json", "payload", payloadJSON)

	qrASCII, err := server.QRCodeASCII()
	if err != nil {
		logger.Warn("failed to render terminal qr code", "error", err)
		return
	}
	fmt.Println(qrASCII)
}
