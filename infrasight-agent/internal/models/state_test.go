package models

import (
	"encoding/json"
	"strings"
	"testing"
	"time"
)

func TestConnectionStepJSONContract(t *testing.T) {
	raw, err := json.Marshal(NewConnectionStep("agent-a"))
	if err != nil {
		t.Fatalf("marshal connection step: %v", err)
	}

	jsonText := string(raw)
	if !strings.Contains(jsonText, `"type":"connection"`) {
		t.Fatalf("connection step missing type: %s", jsonText)
	}
	if !strings.Contains(jsonText, `"machine_name":"agent-a"`) {
		t.Fatalf("connection step missing machine_name: %s", jsonText)
	}
}

func TestStreamSnapshotJSONContract(t *testing.T) {
	snapshot := Snapshot{
		Machine: Machine{
			Name: "agent-a",
			CPU:  12.5,
			RAM:  42.25,
		},
		Containers: []Container{
			{ID: "abc", Name: "api", Status: "running", CPU: 1.5},
		},
		Timestamp: time.Date(2026, 5, 8, 0, 0, 0, 0, time.UTC),
	}

	raw, err := json.Marshal(NewStreamSnapshot(snapshot))
	if err != nil {
		t.Fatalf("marshal stream snapshot: %v", err)
	}

	var decoded map[string]any
	if err := json.Unmarshal(raw, &decoded); err != nil {
		t.Fatalf("unmarshal stream snapshot: %v", err)
	}

	if _, ok := decoded["containers"]; !ok {
		t.Fatalf("stream snapshot missing containers: %s", string(raw))
	}
	if _, ok := decoded["container"]; ok {
		t.Fatalf("stream snapshot must not contain singular container: %s", string(raw))
	}

	machine, ok := decoded["machine"].(map[string]any)
	if !ok {
		t.Fatalf("stream snapshot machine is missing or invalid: %s", string(raw))
	}
	if _, ok := machine["name"]; ok {
		t.Fatalf("stream snapshot machine must not contain static name: %s", string(raw))
	}
}
