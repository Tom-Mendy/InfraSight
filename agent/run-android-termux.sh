#!/data/data/com.termux/files/usr/bin/bash

export HOME=/data/data/com.termux/files/home
export PATH=/data/data/com.termux/files/usr/bin:$PATH
export GOCACHE=$HOME/.cache/go-build

cd /data/data/com.termux/files/home/InfraSight/infrasight-agent

go run cmd/main.go
