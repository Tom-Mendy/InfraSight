package config

import (
	"fmt"
	"os"
	"strconv"
	"strings"
	"time"

	"github.com/joho/godotenv"
)

const (
	EnvHost        = "INFRASIGHT_HOST"
	EnvPort        = "INFRASIGHT_PORT"
	EnvMachineName = "INFRASIGHT_NAME"
	EnvAdvertiseIP = "INFRASIGHT_ADVERTISE_IP"
	EnvInterval    = "INFRASIGHT_INTERVAL"
	EnvDocker      = "INFRASIGHT_DOCKER_ENABLED"
)

// Config contains runtime options for the InfraSight agent.
type Config struct {
	Host           string
	Port           int
	MachineName    string
	AdvertiseIP    string
	UpdateInterval time.Duration
	DockerEnabled  bool
}

// Load reads .env (if present) and environment variables.
// Environment variables already set in the shell keep priority over .env values.
func Load() (Config, error) {
	if err := godotenv.Load(); err != nil && !os.IsNotExist(err) {
		return Config{}, fmt.Errorf("load .env file: %w", err)
	}

	hostname, err := os.Hostname()
	if err != nil || strings.TrimSpace(hostname) == "" {
		hostname = "infrasight-agent"
	}

	cfg := Config{
		Host:           envOrDefault(EnvHost, "0.0.0.0"),
		Port:           8080,
		MachineName:    envOrDefault(EnvMachineName, hostname),
		AdvertiseIP:    strings.TrimSpace(os.Getenv(EnvAdvertiseIP)),
		UpdateInterval: time.Second,
		DockerEnabled:  true,
	}

	if rawPort := strings.TrimSpace(os.Getenv(EnvPort)); rawPort != "" {
		port, err := strconv.Atoi(rawPort)
		if err != nil || port <= 0 || port > 65535 {
			return Config{}, fmt.Errorf("%s must be a valid port (1-65535): %q", EnvPort, rawPort)
		}
		cfg.Port = port
	}

	if rawInterval := strings.TrimSpace(os.Getenv(EnvInterval)); rawInterval != "" {
		interval, err := time.ParseDuration(rawInterval)
		if err != nil || interval <= 0 {
			return Config{}, fmt.Errorf("%s must be a positive duration (example: 1s): %q", EnvInterval, rawInterval)
		}
		cfg.UpdateInterval = interval
	}

	if rawDocker := strings.TrimSpace(os.Getenv(EnvDocker)); rawDocker != "" {
		enabled, err := strconv.ParseBool(rawDocker)
		if err != nil {
			return Config{}, fmt.Errorf("%s must be true/false: %q", EnvDocker, rawDocker)
		}
		cfg.DockerEnabled = enabled
	}

	return cfg, nil
}

func envOrDefault(name, defaultValue string) string {
	value := strings.TrimSpace(os.Getenv(name))
	if value == "" {
		return defaultValue
	}
	return value
}
