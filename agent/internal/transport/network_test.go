package transport

import (
	"errors"
	"testing"
)

func TestResolveAdvertiseIPPrefersExplicitIP(t *testing.T) {
	got, err := resolveAdvertiseIP("0.0.0.0", " 192.168.0.7 ")
	if err != nil {
		t.Fatalf("resolveAdvertiseIP returned error: %v", err)
	}
	if got != "192.168.0.7" {
		t.Fatalf("resolveAdvertiseIP = %q, want %q", got, "192.168.0.7")
	}
}

func TestDetectLocalIPv4PrefersPrimaryRoute(t *testing.T) {
	originalPrimary := detectPrimaryIPv4
	originalFallback := detectFallbackIPv4
	t.Cleanup(func() {
		detectPrimaryIPv4 = originalPrimary
		detectFallbackIPv4 = originalFallback
	})

	detectPrimaryIPv4 = func() (string, error) { return "192.168.0.7", nil }
	detectFallbackIPv4 = func() (string, error) { return "172.23.64.1", nil }

	got, err := detectLocalIPv4()
	if err != nil {
		t.Fatalf("detectLocalIPv4 returned error: %v", err)
	}
	if got != "192.168.0.7" {
		t.Fatalf("detectLocalIPv4 = %q, want primary route IP", got)
	}
}

func TestDetectLocalIPv4FallsBackWhenPrimaryRouteFails(t *testing.T) {
	originalPrimary := detectPrimaryIPv4
	originalFallback := detectFallbackIPv4
	t.Cleanup(func() {
		detectPrimaryIPv4 = originalPrimary
		detectFallbackIPv4 = originalFallback
	})

	detectPrimaryIPv4 = func() (string, error) { return "", errors.New("no route") }
	detectFallbackIPv4 = func() (string, error) { return "192.168.0.7", nil }

	got, err := detectLocalIPv4()
	if err != nil {
		t.Fatalf("detectLocalIPv4 returned error: %v", err)
	}
	if got != "192.168.0.7" {
		t.Fatalf("detectLocalIPv4 = %q, want fallback IP", got)
	}
}
