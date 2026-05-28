#!/usr/bin/env bash
# Renders scaling curves from datapoints emitted by bench-aggregate.sh across runs.
# Each size-<N>-server.json / size-<N>-clients.json holds per-scenario metrics; this renders
# one table per benchmark scenario (StateReplication, PlayerMovement, ...).
#
# Usage: bench-scaling.sh <datapoints_dir> <window_s> <objects>
set -euo pipefail

DP_DIR="${1:?datapoints dir}"
WINDOW="${2:-?}"
OBJECTS="${3:-?}"

SUMMARY="${GITHUB_STEP_SUMMARY:-/dev/stdout}"

shopt -s nullglob
FILES=("$DP_DIR"/size-*-server.json "$DP_DIR"/size-*-clients.json)

{
  echo "## Benchmark Scaling Curve"
  echo ""
  echo "Window: ${WINDOW}s · Objects: ${OBJECTS}"
  echo ""
} >> "$SUMMARY"

if [ ${#FILES[@]} -eq 0 ]; then
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
  def avg(f): if length == 0 then null else (map(f) | add) / length end;

  # server[scenario][connections] = server benchmark ; client[scenario][connections] = measured[]
  (reduce (.[] | select(has("serverScenarios"))) as $f ({};
     reduce ($f.serverScenarios | to_entries[]) as $e (.;
       .[$e.key][$f.connections | tostring] = $e.value))) as $srv
  | (reduce (.[] | select(has("clientScenarios"))) as $f ({};
       reduce ($f.clientScenarios | to_entries[]) as $e (.;
         .[$e.key][$f.connections | tostring] = $e.value))) as $cli
  | ([ $srv, $cli | keys[] ] | unique) as $scenarios
  | $scenarios[]
  | . as $name
  | "### \($name)\n\n"
    + "| Connections | Down payload | Down on-wire | Overhead | CPU | Loop rate | Frame p95 | GC | Heap | Peak RSS | Per-conn down | RTT p95 |\n"
    + "|---|---|---|---|---|---|---|---|---|---|---|---|\n"
    + ( [ (($srv[$name] // {}) + ($cli[$name] // {}) | keys | map(tonumber) | unique | sort)[]
          | . as $n | ($n|tostring) as $k
          | ($srv[$name][$k]) as $s
          | ($cli[$name][$k] // []) as $c
          | "| \($n) "
            + "| \($s.sentBytesPerSec|hbR) "
            + "| \($s.onWireSentBytesPerSec|hbR) "
            + "| \($s.framingOverheadPercent|r1) "
            + "| \($s.serverCpuPercent|r1) "
            + "| \($s.avgFps|if . == null then "-" else floor end) fps "
            + "| \($s.p95TickMs|r2) "
            + "| \($s.gcCollections // "-") "
            + "| \($s.managedHeapBytes|mb) "
            + "| \($s.peakMemoryBytes|mb) "
            + "| \($c | avg(.receivedBytesPerSec) | hbR) "
            + "| \($c | avg(.rttP95Ms) | r2) |"
        ] | join("\n") )
    + "\n"
' "${FILES[@]}" >> "$SUMMARY" || echo "_Failed to render datapoints._" >> "$SUMMARY"

echo "" >> "$SUMMARY"
