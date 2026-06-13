# End-to-end recording of `skillz remove` (interactive removal).
#
# Hidden setup installs the three sample skills. The demo then drives the real
# interactive TUI: the multi-select picker (skills are listed sorted, so the
# order is deterministic), the y/n confirmation (default no), and the removal
# summary. Produces remove-flow.gif + remove-flow.txt.

Output remove-flow.gif
Output remove-flow.txt

Set Shell bash
Set FontSize 18
Set Width 1100
Set Height 850
Set Padding 14
Set TypingSpeed 50ms
Set CursorBlink false

# --- setup (hidden): install three skills so there is something to remove ---
Hide
Type `export PS1="$ "` Enter
Type `export PATH=/src/test/e2e/bin:$PATH` Enter
Type `cd /tmp && rm -rf work && mkdir work && cd work` Enter
Type `cp -r /src/test/fixtures/sample-skills .` Enter
Type `skillz add ./sample-skills -y` Enter
Sleep 1800ms
Type `clear` Enter
Show

# --- the demo ---
Type `skillz remove` Enter

# Multi-select: pick all three (sorted: changelog-updater, commit-helper, pr-describer).
Wait+Screen@30s /Select skills to remove/
Sleep 800ms
Space
Sleep 350ms
Down
Sleep 250ms
Space
Sleep 350ms
Down
Sleep 250ms
Space
Sleep 600ms
Enter

# Confirmation prompt (default no): type y to confirm.
Wait+Screen@30s /Are you sure/
Sleep 700ms
Type `y`
Sleep 350ms
Enter

# Result.
Wait+Screen@30s /Successfully removed/
Sleep 1800ms
Screenshot remove-flow-final.png
Sleep 500ms
