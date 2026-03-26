# InfraSight

## 1\. Overview

InfraSight is a mobile Augmented Reality (AR) application that allows users to visualize and interact with the state of one or multiple machines (CPU, RAM, Docker containers) in real time.

The system is composed of:

- A Go-based agent running on each machine
- A Unity mobile AR application
- A WebSocket-based real-time communication layer

---

## 2\. Objectives

### Primary Goal

Provide a simple and stable AR visualization of a machine’s state (CPU, RAM, Docker containers).

### Secondary Goal

Design the system so it can evolve into a DevOps visualization tool (multi-machine, interaction, Kubernetes).

---

## 3\. Architecture

### High-Level Architecture

Machine (Agent) → WebSocket → Mobile AR App

### Components

#### 3.1 Agent (Go)

- Collects system metrics
- Normalizes data
- Streams updates via WebSocket

#### 3.2 Mobile App (Unity)

- Connects to agents via WebSocket
- Maintains a local state
- Renders 3D objects in AR
- Handles user interaction

---

## 4\. Backend Design (Go Agent)

### Responsibilities

- Collect metrics (CPU, RAM, Docker)
- Expose WebSocket endpoint
- Broadcast updates at fixed intervals (1s)

### Data Model

| {  "machine": {    "name": "laptop",    "cpu": 42,    "ram": 68  },  "containers": \[    {      "id": "abc",      "name": "api",      "status": "running",      "cpu": 12    }  \]} |
| :---- |

### Project Structure

| agent/  cmd/main.go  internal/    collector/    models/    service/    transport/ |
| :---- |

### Key Loop

- Collect data
- Build state object
- Broadcast via WebSocket

---

## 5\. Frontend Design (Unity)

### Responsibilities

- Scan QR codes
- Connect to WebSocket endpoints
- Maintain application state
- Render AR objects

### Layers

#### Network Layer

- WebSocket client
- Handles connection per machine

#### State Layer

- Stores latest received data
- One state per machine

#### Rendering Layer

- Converts state into 3D objects

#### Interaction Layer

- Handles user input (tap, select)

---

## 6\. Multi-Machine Support

### Concept

Each machine runs its own agent.

The mobile app can connect to multiple agents simultaneously.

### Implementation

- One WebSocket connection per machine
- One 3D object per machine

### Spatial Layout

- Machines are positioned in different areas of the AR space
- Avoid overlap for readability

---

## 7\. QR Code Connection System

### Goal

Provide a simple way to connect a mobile device to a machine.

### Flow

1. Agent generates a QR code containing connection data
2. User scans QR code using the mobile app
3. App extracts WebSocket URL
4. App connects automatically
5. Machine appears in AR

### QR Payload Example

| {  "name": "Tom-Laptop",  "ip": "192.168.1.10",  "port": 8080} |
| :---- |

### Advantages

- No manual configuration
- Fast onboarding
- Good user experience

---

## 8\. User Journey

### Scenario: Connect and visualize a machine

1. User launches the agent on their computer
2. Agent displays a QR code
3. User opens the mobile AR app
4. User scans the QR code
5. App connects to the machine
6. A 3D representation appears above the computer
7. User sees live CPU and RAM usage

---

## 9\. Visualization Design

### Machine Representation

- Sphere
- Size \= CPU usage
- Color \= RAM usage

### Containers

- Orbit around the machine
- Color:
  - Green \= running
  - Red \= stopped

---

## 10\. Technical Constraints

### Network

- Same WiFi network required
- Open port (default: 8080\)

### Performance

- Update frequency: 1 message per second
- Avoid flooding WebSocket

### Compatibility

- Mobile AR via Unity AR Foundation

---

## 11\. Limitations (Phase A)

- No authentication
- Local network only
- Limited to CPU, RAM, Docker

---

## 12\. Future Improvements (Phase B)

### Visualization

- Animated objects
- Alerts for high CPU usage

### Interaction

- Restart Docker container from AR

### Infrastructure

- Multi-machine visualization
- Kubernetes integration

### Security

- Token-based authentication via QR code

---

## 13\. Technical Choices

### Why Go (Backend Agent)

- Excellent performance for real-time data collection
- Strong concurrency model (goroutines) for collectors and WebSocket broadcasting
- Simple deployment (single binary)
- Good ecosystem for system metrics and Docker integration

### Why WebSocket

- Real-time bidirectional communication
- Lower latency compared to HTTP polling
- Efficient for continuous streaming of metrics
- Enables future interaction (sending actions from mobile to agent)

### Why Unity (Mobile AR)

- Strong AR support via AR Foundation (cross-platform)
- Mature ecosystem for 3D rendering and interaction
- Easy integration of external libraries (QR scanning, networking)
- Suitable for rapid prototyping and visual iteration

---

## 14\. Network Diagram

| \[ Laptop / Server \]        │        │ runs        ▼   \[ Go Agent \]        │        │ WebSocket (ws://IP:PORT/ws)        ▼   \[ Mobile AR App \]        │        ▼   \[ AR Visualization \] |
| :---- |

### Multi-Machine Extension

| \[ Machine A \]   \[ Machine B \]   \[ Machine C \]     │               │               │     ▼               ▼               ▼ \[ Agent A \]     \[ Agent B \]     \[ Agent C \]      \\             |             /       \\            |            /        \\           |           /         ▼          ▼          ▼           \[ Mobile AR App \]                 │                 ▼           \[ AR Scene \] |
| :---- |

---

## 15\. AR Visual Mapping Diagram

|            (Container)               ●                \\                 \\        ●----●----●   ← containers orbit              |              |           \[ O \]  ← Machine (sphere)          /  |  \\         /   |   \\   CPU → scale   RAM → color |
| :---- |

### Mapping Rules

- Machine \= central sphere
- CPU usage \= size (scale)
- RAM usage \= color (green → red)
- Containers \= orbiting objects
- Container status \= color (green/red)

---

## 16\. README Structure (Final)

| \# InfraSight\#\# Description\#\# Demo\#\# Architecture\#\# Technical Choices\#\# Installation\#\# Usage\#\# Network Requirements\#\# Limitations\#\# Future Work |
| :---- |

---

## 17\. Validation Checklist

### Functional

- [ ] Agent runs and exposes metrics
- [ ] WebSocket connection works
- [ ] Mobile app connects via QR code
- [ ] CPU/RAM displayed in AR
- [ ] Docker containers visible

### Technical

- [ ] Clean project structure
- [ ] Separation backend / frontend
- [ ] Stable WebSocket (no crashes)
- [ ] Data model consistent

### UX

- [ ] AR object stable in space
- [ ] Data updates smoothly
- [ ] Clear visual mapping

### Documentation

- [ ] README complete
- [ ] Architecture explained
- [ ] Setup instructions working

---

## 18\. Conclusion

InfraSight is designed as a simple but extensible system. The initial version focuses on stability and clarity, while the architecture allows future extensions into a full DevOps visualization and interaction tool.

The project demonstrates:

- Real-time systems (WebSocket)
- Backend development in Go
- AR application development with Unity
- DevOps-oriented thinking (Docker, observability)
