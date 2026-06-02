import argparse
import http.server
import socket
import socketserver
import threading
import time
import urllib.request


PORT = 8080
PAYLOAD = b"x" * 8192


class Handler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(len(PAYLOAD)))
        self.end_headers()
        self.wfile.write(PAYLOAD)

    def log_message(self, format, *args):
        return


class ReusableTCPServer(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def serve():
    with ReusableTCPServer(("0.0.0.0", PORT), Handler) as server:
        server.serve_forever()


def request_loop(name, peer):
    url = f"http://{peer}:{PORT}/"
    while True:
        try:
            with urllib.request.urlopen(url, timeout=2) as response:
                response.read()
            print(f"{name} -> {peer}", flush=True)
        except Exception as exc:
            print(f"{name} waiting for {peer}: {exc}", flush=True)
        time.sleep(0.25)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--name", required=True)
    parser.add_argument("--peer", required=True)
    args = parser.parse_args()

    socket.setdefaulttimeout(2)
    threading.Thread(target=serve, daemon=True).start()
    request_loop(args.name, args.peer)


if __name__ == "__main__":
    main()
