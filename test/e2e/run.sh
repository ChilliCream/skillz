#!/usr/bin/env bash
# Record the skillz CLI flows with VHS and verify each against its golden frame.
#
#   ./run.sh                  record every flow, diff each final frame vs its golden (CI / test)
#   ./run.sh --update         record every flow, overwrite goldens + committed GIFs (accept output)
#   ./run.sh remove update    record only the named flows (any subset of the list below)
#   ./run.sh --update init    update a single flow
#
# Each flow <name> is driven by test/e2e/<name>-flow.tape and checked against
# test/e2e/<name>-flow.golden.txt. One VHS run per flow produces both the demo
# GIF (<name>-flow.gif) and the text capture that is reduced to a deterministic
# final frame and diffed.
#
# Inputs (the repo) are mounted read-only into the pinned VHS container; the
# container bundles ttyd + ffmpeg + fonts so nothing needs installing on the host.
# The published binary and raw recordings land in gitignored dirs (bin/, out/).
#
# Results for changed/new flows are collected under out/report/ for CI to upload
# as artifacts and surface in a PR comment:
#   out/report/status.tsv      <flow>\t<PASS|FAIL|NEW>   (every recorded flow)
#   out/report/changed.txt     one <flow> per line       (FAIL or NEW only)
#   out/report/<flow>-flow.gif / .frame.txt / .golden.txt / .diff
#
# Exit code: 0 if every recorded flow PASSed; 1 if any FAILed or was NEW (no
# golden yet); 2 on usage/setup error; 3 if docker is unavailable. --update
# always exits 0 on a successful recording.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
OUT_DIR="$SCRIPT_DIR/out"
BIN_DIR="$SCRIPT_DIR/bin"
REPORT_DIR="$OUT_DIR/report"

# Pinned by digest so a new VHS release can't silently shift the rendered output
# (and thus the goldens). Bump deliberately, then re-run with --update. Tag: v0.11.0.
VHS_IMAGE="${VHS_IMAGE:-ghcr.io/charmbracelet/vhs@sha256:9d5fc3dc0c160b0fb1d2212baff07e6bdf3fa9438c504a3237484567302fcf93}"

# Every flow, in record order, with the literal marker the completed final frame
# must contain (see extract-frame.sh). Keep this list and ALL_FLOWS in sync.
declare -A MARKERS=(
  [add]="Done!"
  [copy]="Done!"
  [global]="Done!"
  [init]="Initialized skill"
  [list]="Project Skills"
  [remove]="Successfully removed"
  [update]="No project skills to update."
  [error]="Local path does not exist"
)
ALL_FLOWS=(add copy global init list remove update error)

# --- args: optional --update plus an optional subset of flow names ------------
UPDATE=0
FLOWS=()
for arg in "$@"; do
  case "$arg" in
    --update) UPDATE=1 ;;
    -*) echo "unknown option: $arg" >&2; exit 2 ;;
    *)
      if [[ -z "${MARKERS[$arg]+x}" ]]; then
        echo "unknown flow: $arg (known: ${ALL_FLOWS[*]})" >&2
        exit 2
      fi
      FLOWS+=("$arg")
      ;;
  esac
done
[[ ${#FLOWS[@]} -eq 0 ]] && FLOWS=("${ALL_FLOWS[@]}")

if ! command -v docker >/dev/null 2>&1; then
  echo "docker is required to run the VHS recordings" >&2
  exit 3
fi

# 1. Publish a self-contained linux-x64 binary (glibc — matches the Debian-based
#    VHS image). Skip if already present unless REBUILD=1.
if [[ ! -x "$BIN_DIR/skillz" || "${REBUILD:-0}" == "1" ]]; then
  echo "==> publishing skillz (linux-x64, self-contained)"
  dotnet publish "$REPO_ROOT/src/Skillz/Skillz.csproj" \
    -c Release -f net10.0 -r linux-x64 --self-contained \
    -p:PublishAot=false -p:PublishSingleFile=true -p:PublishTrimmed=false \
    -o "$BIN_DIR" >/dev/null
fi

mkdir -p "$OUT_DIR"
rm -rf "$REPORT_DIR"
mkdir -p "$REPORT_DIR"
: > "$REPORT_DIR/status.tsv"
: > "$REPORT_DIR/changed.txt"

overall=0
declare -a SUMMARY=()

for flow in "${FLOWS[@]}"; do
  tape="$SCRIPT_DIR/${flow}-flow.tape"
  golden="$SCRIPT_DIR/${flow}-flow.golden.txt"
  frame="$OUT_DIR/${flow}-flow.frame.txt"

  if [[ ! -f "$tape" ]]; then
    echo "==> $flow: missing tape ($tape)" >&2
    exit 2
  fi

  # 2. Record. The tape writes <flow>-flow.gif + <flow>-flow.txt into OUT_DIR
  #    (mounted as the VHS workdir at /vhs).
  echo "==> recording ${flow}-flow"
  docker run --rm --shm-size=512m \
    -v "$REPO_ROOT":/src:ro \
    -v "$OUT_DIR":/vhs \
    "$VHS_IMAGE" "/src/test/e2e/${flow}-flow.tape"

  # 3. Reduce the multi-frame capture to the deterministic final frame.
  bash "$SCRIPT_DIR/extract-frame.sh" "$OUT_DIR/${flow}-flow.txt" "${MARKERS[$flow]}" > "$frame"

  # 4. Update or verify.
  if [[ "$UPDATE" == "1" ]]; then
    cp "$frame" "$golden"
    cp "$OUT_DIR/${flow}-flow.gif" "$SCRIPT_DIR/${flow}-flow.gif"
    echo "    updated golden + GIF"
    SUMMARY+=("$flow UPDATED")
    continue
  fi

  if [[ ! -f "$golden" ]]; then
    status=NEW
  elif diff -u "$golden" "$frame" > "$REPORT_DIR/${flow}-flow.diff" 2>&1; then
    status=PASS
    rm -f "$REPORT_DIR/${flow}-flow.diff"
  else
    status=FAIL
  fi

  printf '%s\t%s\n' "$flow" "$status" >> "$REPORT_DIR/status.tsv"
  SUMMARY+=("$flow $status")

  if [[ "$status" != "PASS" ]]; then
    overall=1
    echo "$flow" >> "$REPORT_DIR/changed.txt"
    cp -f "$OUT_DIR/${flow}-flow.gif" "$REPORT_DIR/${flow}-flow.gif" 2>/dev/null || true
    cp -f "$frame" "$REPORT_DIR/${flow}-flow.frame.txt" 2>/dev/null || true
    [[ -f "$golden" ]] && cp -f "$golden" "$REPORT_DIR/${flow}-flow.golden.txt"
    echo "    $status (recorded; see report)"
  else
    echo "    PASS"
  fi
done

echo
echo "==> summary"
for line in "${SUMMARY[@]}"; do
  echo "    $line"
done

if [[ "$UPDATE" == "1" ]]; then
  exit 0
fi

if [[ "$overall" -ne 0 ]]; then
  echo "==> FAIL: $(wc -l < "$REPORT_DIR/changed.txt" | tr -d ' ') flow(s) changed or new (re-run with --update to accept)" >&2
else
  echo "==> PASS: all ${#FLOWS[@]} flow(s) match their golden"
fi
exit "$overall"
