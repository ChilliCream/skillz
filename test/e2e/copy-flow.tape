# End-to-end recording of `skillz add --copy` (copy instead of symlink).
#
# Non-interactive (--agent + --skill + -y skip every prompt). Validates the
# copy install path and the "Copied:" summary label — the counterpart to
# add-flow.tape, which shows the interactive symlink path. A single skill is
# pinned with --skill so the install report is byte-deterministic (the report
# lists skills in discovery order, which is filesystem-dependent for >1 skill).
#
# Produces copy-flow.gif (demo) + copy-flow.txt (final-frame golden source).

Output copy-flow.gif
Output copy-flow.txt

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
Type `cp -r /src/test/fixtures/sample-skills .` Enter
Type `clear` Enter
Show

# --- the demo ---
Type `skillz add ./sample-skills --agent claude-code --copy --skill commit-helper -y` Enter

Wait+Screen@30s /Done/
Sleep 1800ms
Screenshot copy-flow-final.png
Sleep 500ms
