package transport

import (
	"fmt"
	"net"
	"strings"
)

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
