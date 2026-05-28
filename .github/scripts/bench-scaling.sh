#!/usr/bin/env bash
# Renders the scaling curve from datapoints emitted by bench-aggregate.sh across runs.
# Reads size-<N>-server.json / size-<N>-clients.json files and renders one combined table.
#
# Usage: bench-scaling.sh <datapoints_dir> <window_s> <objects>
set -euo pipefail

DP_DIR="${1:?datapoints dir}"
WINDOW="${2:-?}"
OBJECTS="${3:-?}"

SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

shopt -s nullglob
SRV=("$DP_DIR"/size-*-server.json)
CLI=("$DP_DIR"/size-*-clients.json)

{
  echo "## Benchmark Scaling Curve"
  echo ""
  echo "Window: ${WINDOW}s · Objects: ${OBJECTS}"
  echo ""
  echo "| Connections | Down payload | Down on-wire | Overhead | CPU | Loop rate | Frame p95 | GC | Heap | Peak RSS | Per-conn down | RTT p95 |"
  echo "|---|---|---|---|---|---|---|---|---|---|---|---|"
} >> "$SUMMARY"

if [ ${#SRV[@]} -eq 0 ] && [ ${#CLI[@]} -eq 0 ]; then
  echo "_No datapoints collected._" >> "$SUMMARY"
  exit 0
fi

jq -rs '
  def hbR:
    if . == null then "-"
    elif . < 1024 then "\(.|floor) B/s"
    elif . < 1048576 then "\((.*10/1024|floor)/10) KB/s"
    else "\((.*100/1048576|floor)/100) MB/s" end;
  def r2: if . == null then "-" else "\((.*100|floor)/100) ms" end;
  def r1: if . == null then "-" else "\((.*10|floor)/10)%" end;
  def mb: if . == null then "-" else "\((./1048576)|floor) MB" end;

  ([ .[] | select(has("server") and .server != null) ]
     | map({key: (.connections|tostring), value: .server}) | from_entries) as $srv
  | ([ .[] | select(has("measured")) ]
      | map({ key: (.connections|tostring),
              value: ( (.measured | length) as $n
                       | if $n == 0 then null else
                           { recv: ((.measured | map(.receivedBytesPerSec) | add) / $n),
                             p95:  ((.measured | map(.rttP95Ms) | add) / $n) }
                         end ) })
      | from_entries) as $cli
  | ([ .[].connections ] | unique | sort) as $sizes
  | $sizes[]
  | (.|tostring) as $k
  | ($srv[$k]) as $s
  | ($cli[$k]) as $c
  | "| \(.) "
    + "| \($s.sentBytesPerSec|hbR) "
    + "| \($s.onWireSentBytesPerSec|hbR) "
    + "| \($s.framingOverheadPercent|r1) "
    + "| \($s.serverCpuPercent|r1) "
    + "| \($s.avgFps|if . == null then "-" else floor end) fps "
    + "| \($s.p95TickMs|r2) "
    + "| \($s.gcCollections // "-") "
    + "| \($s.managedHeapBytes|mb) "
    + "| \($s.peakMemoryBytes|mb) "
    + "| \($c.recv|hbR) "
    + "| \($c.p95|r2) |"
' "${SRV[@]}" "${CLI[@]}" >> "$SUMMARY" || echo "_Failed to render datapoints._" >> "$SUMMARY"

echo "" >> "$SUMMARY"
