# End-to-end recording of an error path: `skillz add` on a missing local source.
#
# Validates the failure UX end to end — the logo, the resolved Source line, and
# the error message (written to stderr, exit code 1). Hermetic: the path simply
# does not exist. Produces error-flow.gif + error-flow.txt.

Output error-flow.gif
Output error-flow.txt

Set Shell bash
Set FontSize 18
Set Width 1100
Set Height 850
Set Padding 14
Set TypingSpeed 50ms
Set CursorBlink false

# --- setup (hidden) ---
Hide
Type `export PS1="$ "` Enter
Type `export PATH=/src/test/e2e/bin:$PATH` Enter
Type `cd /tmp && rm -rf work && mkdir work && cd work` Enter
Type `clear` Enter
Show

# --- the demo ---
Type `skillz add ./missing` Enter

Wait+Screen@30s /Local path does not exist/
Sleep 1500ms
Screenshot error-flow-final.png
Sleep 500ms
