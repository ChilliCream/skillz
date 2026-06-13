# End-to-end recording of `skillz update` (interactive update check).
#
# Hidden setup installs the three sample skills from a LOCAL source (so the lock
# records them) — local skills are never network-checked, which keeps the flow
# hermetic and deterministic. The demo drives the interactive scope picker
# (Project / Global / Both), selects Both, and shows the "nothing to update"
# result. Produces update-flow.gif + update-flow.txt.

Output update-flow.gif
Output update-flow.txt

Set Shell bash
Set FontSize 18
Set Width 1100
Set Height 850
Set Padding 14
Set TypingSpeed 50ms
Set CursorBlink false

# --- setup (hidden): install three local skills so the lock is populated ---
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
Type `skillz update` Enter

# Scope picker (single-select): default is Project; arrow down twice to Both.
Wait+Screen@30s /Update scope/
Sleep 800ms
Down
Sleep 300ms
Down
Sleep 300ms
Enter

# Result.
Wait+Screen@30s /No project skills to update/
Sleep 1500ms
Screenshot update-flow-final.png
Sleep 500ms
