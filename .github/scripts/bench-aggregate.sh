#!/usr/bin/env bash
# Renders a single benchmark run (one connection count) to the job summary and emits
# compact scaling datapoints for the scaling-curve workflow to collect.
#
# Usage: bench-aggregate.sh <results_dir> <total_connections> <tag> <window_s> <objects> <scaling_out_dir>
set -euo pipefail

RESULTS_DIR="${1:?results dir}"
TOTAL="${2:?total connections}"
TAG="${3:-solo}"
WINDOW="${4:-?}"
OBJECTS="${5:-?}"
SCALING_DIR="${6:-scaling}"

SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/stdout}"
mkdir -p "$SCALING_DIR"

SERVER_FILE="$RESULTS_DIR/server.json"
shopt -s nullglob
CLIENT_FILES=("$RESULTS_DIR"/client-*.json)

JQ_LIB='
def hbR:
  if . == null then "-"
  elif . < 1024 then "\(.|floor) B/s"
  elif . < 1048576 then "\((.*10/1024|floor)/10) KB/s"
  else "\((.*100/1048576|floor)/100) MB/s" end;
def hbT:
  if . == null then "-"
  elif . < 1024 then "\(.|floor) B"
  elif . < 1048576 then "\((.*10/1024|floor)/10) KB"
  else "\((.*100/1048576|floor)/100) MB" end;
def r2: if . == null then "-" else "\((.*100|floor)/100) ms" end;
def r1: if . == null then "-" else "\((.*10|floor)/10)%" end;
def benchOf: [ .[] | select(.benchmark != null) | .benchmark ] | (if length > 0 then .[0] else null end);
'

{
  echo "## Benchmark — ${TOTAL} connections (tag: ${TAG})"
  echo ""
  echo "Window: ${WINDOW}s · Objects: ${OBJECTS} · Measured clients: ${#CLIENT_FILES[@]}"
  echo ""
} >> "$SUMMARY"

if [ -f "$SERVER_FILE" ]; then
  jq -r "$JQ_LIB"'
    benchOf as $s
    | if $s == null then "_No server benchmark data._\n" else
      "### Server\n\n| Metric | Value |\n|---|---|\n"
      + "| Downstream payload | \($s.sentBytesPerSec|hbR) |\n"
      + "| Downstream on-wire | \($s.onWireSentBytesPerSec|hbR) |\n"
      + "| Upstream payload | \($s.receivedBytesPerSec|hbR) |\n"
      + "| Upstream on-wire | \($s.onWireReceivedBytesPerSec|hbR) |\n"
      + "| Framing overhead | \($s.framingOverheadPercent|r1) |\n"
      + "| Packets sent | \($s.nativePacketsSentPerSec|floor)/s |\n"
      + "| Packet loss | \($s.packetLoss) |\n"
      + "| Connections | \($s.connectionCount) |\n"
      + "| Replicated objects | \($s.objectCount) |\n"
      + "| CPU | \($s.serverCpuPercent|r1) |\n"
      + "| Loop rate | \($s.avgFps|floor) fps |\n"
      + "| Avg frame | \($s.avgTickMs|r2) |\n"
      + "| Frame p95 / p99 | \($s.p95TickMs|r2) / \($s.p99TickMs|r2) |\n"
      + "| Max frame | \($s.maxTickMs|r2) |\n"
      + "| GC collections | \($s.gcCollections) |\n"
      + "| Managed heap | \(($s.managedHeapBytes/1048576)|floor) MB |\n"
      + "| Peak RSS | \(($s.peakMemoryBytes/1048576)|floor) MB |\n"
      end
  ' "$SERVER_FILE" >> "$SUMMARY"
  echo "" >> "$SUMMARY"

  # Bandwidth attribution: which RPC/broadcast types account for the traffic.
  jq -r "$JQ_LIB"'
    benchOf as $s
    | ($s.bandwidthBreakdown // []) as $b
    | if ($b | length) == 0 then "" else
      "#### Bandwidth by type (server, window total)\n\n"
      + "| Kind | Type | Sent (msgs) | Sent | Recv (msgs) | Recv |\n|---|---|---|---|---|---|\n"
      + ( [ limit(15; $b[]) | "| \(.kind) | \(.name) | \(.sentCount) | \(.sentBytes|hbT) | \(.recvCount) | \(.recvBytes|hbT) |" ] | join("\n") )
      + "\n"
      end
  ' "$SERVER_FILE" >> "$SUMMARY"
  echo "" >> "$SUMMARY"

  jq -c "$JQ_LIB"'{connections: '"$TOTAL"', server: benchOf}' "$SERVER_FILE" \
    > "$SCALING_DIR/size-${TOTAL}-server.json" || true
fi

if [ ${#CLIENT_FILES[@]} -gt 0 ]; then
  jq -rs "$JQ_LIB"'
    [ .[][] | select(.benchmark != null and .benchmark.measured == true) | .benchmark ] as $b
    | ($b | length) as $n
    | if $n == 0 then "_No measured client data._\n" else
      ($b | map(.receivedBytesPerSec) | add / $n) as $recv
    | ($b | map(.sentBytesPerSec) | add / $n) as $sent
    | ($b | map(.onWireReceivedBytesPerSec) | add / $n) as $recvWire
    | ($b | map(.packetLoss) | add) as $loss
    | ($b | map(.rttP50Ms) | add / $n) as $p50
    | ($b | map(.rttP95Ms) | add / $n) as $p95
    | ($b | map(.rttP99Ms) | add / $n) as $p99
    | "### Measured clients (avg of \($n))\n\n| Metric | Value |\n|---|---|\n"
      + "| Per-conn downstream (payload) | \($recv|hbR) |\n"
      + "| Per-conn downstream (on-wire) | \($recvWire|hbR) |\n"
      + "| Per-conn upstream (payload) | \($sent|hbR) |\n"
      + "| RTT p50 | \($p50|r2) |\n"
      + "| RTT p95 | \($p95|r2) |\n"
      + "| RTT p99 | \($p99|r2) |\n"
      + "| Total packet loss (all clients) | \($loss) |\n"
      end
  ' "${CLIENT_FILES[@]}" >> "$SUMMARY"
  echo "" >> "$SUMMARY"

  jq -s '{connections: '"$TOTAL"', measured: [ .[][] | select(.benchmark != null and .benchmark.measured == true) | .benchmark ]}' \
    "${CLIENT_FILES[@]}" > "$SCALING_DIR/size-${TOTAL}-clients.json" || true
fi
