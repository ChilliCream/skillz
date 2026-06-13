# End-to-end recording of `skillz list` (show installed skills).
#
# Hidden setup installs one skill to a concrete agent (claude-code) so the table
# has a real, deterministic row (Skill / Path / Agents). A single skill keeps the
# output byte-stable. Produces list-flow.gif + list-flow.txt.

Output list-flow.gif
Output list-flow.txt

Set Shell bash
Set FontSize 18
Set Width 1100
Set Height 850
Set Padding 14
Set TypingSpeed 50ms
Set CursorBlink false

# --- setup (hidden): install one skill so `list` has something to show ---
Hide
Type `export PS1="$ "` Enter
Type `export PATH=/src/test/e2e/bin:$PATH` Enter
Type `cd /tmp && rm -rf work && mkdir work && cd work` Enter
Type `cp -r /src/test/fixtures/sample-skills .` Enter
Type `skillz add ./sample-skills --agent claude-code --skill commit-helper -y` Enter
Sleep 1500ms
Type `clear` Enter
Show

# --- the demo ---
Type `skillz list` Enter

Wait+Screen@30s /Project Skills/
Sleep 1500ms
Screenshot list-flow-final.png
Sleep 500ms
