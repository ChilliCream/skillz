#!/usr/bin/env bash
# Extract the final completed-state frame from a VHS .txt capture.
#
#   extract-frame.sh <capture.txt> [marker]
#
# `Output foo.txt` in a tape concatenates every rendered frame, separated by a
# horizontal rule (a line of box-drawing dashes). For a stable golden we keep
# only the last frame that reached the flow's success marker, with trailing
# whitespace and surrounding blank lines trimmed.
#
# `marker` is a literal substring the completed frame must contain (e.g. "Done!"
# for an install, "Successfully removed" for a removal). When omitted/empty the
# last non-blank frame is used. If the marker is never seen we fall back to the
# last non-blank frame anyway, so a wrong/absent marker surfaces as an obvious
# golden mismatch rather than empty output.
set -euo pipefail

TXT="${1:-/dev/stdin}"
MARKER="${2:-}"

awk -v marker="$MARKER" '
  function store() {
    if (buf ~ /[^[:space:]]/) {
      anylast = buf                                   # last non-blank frame (fallback)
      if (marker == "" || index(buf, marker) > 0) {
        last = buf                                    # last frame matching the marker
      }
    }
    buf = ""
  }
  /^────/ { store(); next }
  { line = $0; sub(/[ \t\r]+$/, "", line); buf = buf line "\n" }
  END {
    store()
    out = (last != "" ? last : anylast)
    sub(/^\n+/, "", out)
    sub(/\n+$/, "\n", out)
    printf "%s", out
  }
' "$TXT"
