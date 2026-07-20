#!/usr/bin/env bash
# Renders a benchmark run (one connection count) to the job summary and emits compact per-scenario
# scaling datapoints for the scaling-curve workflow. Multiple benchmark scenarios run sequentially
# in one pass (e.g. StateReplication then PlayerMovement); each is rendered as its own section.
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
def serverTable($name; $s):
  "### Server — \($name)\n\n| Metric | Value |\n|---|---|\n"
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
  + "| Peak RSS | \(($s.peakMemoryBytes/1048576)|floor) MB |\n";
def rep($ch; $n): if $n > 0 then $ch * $n else "" end;
def barOf($frac): ($frac * 20 | floor) as $f | rep("█"; $f) + rep("░"; 20 - $f);
def breakdownTable($s):
  ($s.bandwidthBreakdown // []) as $b
  | ($b | map(.sentBytes) | add) as $tot
  | if ($b | length) == 0 then "" else
    "\n#### Bandwidth by type (server, window total — sorted by share of sent)\n\n"
    + "| Kind | Type | Share of sent | Sent | Sent (msgs) | Recv | Recv (msgs) |\n|---|---|---|---|---|---|---|\n"
    + ( [ limit(15; $b[])
          | (if $tot > 0 then .sentBytes / $tot else 0 end) as $frac
          | "| \(.kind) | \(.name) | `\(barOf($frac))` \(($frac*1000|floor)/10)% | \(.sentBytes|hbT) | \(.sentCount) | \(.recvBytes|hbT) | \(.recvCount) |" ] | join("\n") )
    + "\n"
    end;
# CPU time attribution from PurrNet ProfilerMarkers (Development build only). The bar is relative to
# the hottest marker (markers nest, so this is magnitude, not % of frame). Top 8 also drawn as a chart.
def cpuSection($name; $s):
  ($s.cpuMarkers // []) as $m
  | if ($m | length) == 0 then "" else
    ($m | map(.totalMs) | max) as $maxMs
    | ($m | map({n: (.name | split(".") | .[-1]), v: (.perFrameMs * 1000 | floor)})[0:8]) as $top
    | ($top | map(.v) | max) as $vmax
    | "\n#### CPU by marker — \($name) (Development build)\n\n"
    + "| Marker | Per-frame | Total | Calls | Relative |\n|---|---|---|---|---|\n"
    + ( [ limit(20; $m[])
          | (if $maxMs > 0 then .totalMs / $maxMs else 0 end) as $frac
          | "| \(.name) | \(.perFrameMs*1000|floor) µs | \((.totalMs*100|floor)/100) ms | \(.calls) | `\(barOf($frac))` |" ] | join("\n") )
    + "\n\n```mermaid\nxychart-beta\n"
    + "    title \"CPU per-frame µs by marker — \($name)\"\n"
    + "    x-axis [\($top | map("\"" + .n + "\"") | join(", "))]\n"
    + "    y-axis \"µs/frame\" 0 --> \((($vmax * 1.1) | floor) + 1)\n"
    + "    bar [\($top | map(.v | tostring) | join(", "))]\n"
    + "```\n"
    end;
'

{
  echo "## Benchmark — ${TOTAL} connections (tag: ${TAG})"
  echo ""
  echo "Window: ${WINDOW}s · Objects: ${OBJECTS} · Measured clients: ${#CLIENT_FILES[@]}"
  echo ""
} >> "$SUMMARY"

if [ -f "$SERVER_FILE" ]; then
  jq -r "$JQ_LIB"'
    [ .[] | select(.benchmark != null) ] as $entries
    | if ($entries | length) == 0 then "_No server benchmark data._\n" else
      ( $entries[] | serverTable(.name; .benchmark) + breakdownTable(.benchmark) + cpuSection(.name; .benchmark) + "\n" )
      end
  ' "$SERVER_FILE" >> "$SUMMARY"
  echo "" >> "$SUMMARY"

  jq -c '{connections: '"$TOTAL"',
          serverScenarios: ([ .[] | select(.benchmark != null) | {key: .name, value: .benchmark} ] | from_entries)}' \
    "$SERVER_FILE" > "$SCALING_DIR/size-${TOTAL}-server.json" || true
fi

if [ ${#CLIENT_FILES[@]} -gt 0 ]; then
  jq -rs "$JQ_LIB"'
    [ .[][] | select(.benchmark != null and .benchmark.measured == true) ] as $all
    | ($all | group_by(.name)) as $groups
    | if ($groups | length) == 0 then "_No measured client data._\n" else
      ( $groups[]
        | .[0].name as $name
        | [ .[].benchmark ] as $b
        | ($b | length) as $n
        | ($b | map(.receivedBytesPerSec) | add / $n) as $recv
        | ($b | map(.onWireReceivedBytesPerSec) | add / $n) as $recvWire
        | ($b | map(.sentBytesPerSec) | add / $n) as $sent
        | ($b | map(.packetLoss) | add) as $loss
        | ($b | map(.rttP50Ms) | add / $n) as $p50
        | ($b | map(.rttP95Ms) | add / $n) as $p95
        | ($b | map(.rttP99Ms) | add / $n) as $p99
        | "### Measured clients — \($name) (avg of \($n))\n\n| Metric | Value |\n|---|---|\n"
          + "| Per-conn downstream (payload) | \($recv|hbR) |\n"
          + "| Per-conn downstream (on-wire) | \($recvWire|hbR) |\n"
          + "| Per-conn upstream (payload) | \($sent|hbR) |\n"
          + "| RTT p50 | \($p50|r2) |\n"
          + "| RTT p95 | \($p95|r2) |\n"
          + "| RTT p99 | \($p99|r2) |\n"
          + "| Total packet loss (all clients) | \($loss) |\n\n" )
      end
  ' "${CLIENT_FILES[@]}" >> "$SUMMARY"
  echo "" >> "$SUMMARY"

  jq -s '{connections: '"$TOTAL"',
          clientScenarios: ([ .[][] | select(.benchmark != null and .benchmark.measured == true) ]
                            | group_by(.name) | map({key: .[0].name, value: [ .[].benchmark ]}) | from_entries)}' \
    "${CLIENT_FILES[@]}" > "$SCALING_DIR/size-${TOTAL}-clients.json" || true
fi
