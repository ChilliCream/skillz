# End-to-end recording of `skillz add` driving the real interactive TUI.
#
# Produces two artifacts from ONE run:
#   add-flow.gif  — animated demo for PRs / README (not asserted on)
#   add-flow.txt  — final-frame text capture, scrubbed + diffed against a golden
#
# Run via the pinned VHS container (see run.sh). Inputs are mounted read-only at
# /src; outputs land in VHS's working dir. Every keystroke is gated on a Wait
# sentinel so the recording is synchronised to state, not wall-clock timing.

Output add-flow.gif
Output add-flow.txt

Set Shell bash
Set FontSize 18
Set Width 1100
Set Height 850
Set Padding 14
Set TypingSpeed 50ms
Set CursorBlink false

# --- setup (hidden from the recording) ---
# Clean prompt, put the published binary on PATH, work in a throwaway dir so the
# install writes under /tmp (never into the mounted source tree).
Hide
Type `export PS1="$ "` Enter
Type `export PATH=/src/test/e2e/bin:$PATH` Enter
Type `cd /tmp && rm -rf work && mkdir work && cd work` Enter
Type `cp -r /src/test/fixtures/sample-skills .` Enter
Type `clear` Enter
Show

# --- the demo ---
Type `skillz add ./sample-skills` Enter

# Skill multi-select: pick 2 of the 3 skills (leave changelog-updater off).
Wait+Screen@30s /Select skills to install/
Sleep 800ms
Down
Sleep 400ms
Space
Sleep 400ms
Down
Sleep 400ms
Space
Sleep 700ms
Enter

# Searchable agent picker: type to filter, then accept the pre-selected Claude Code.
Wait+Screen@30s /Which agents/
Sleep 700ms
Type `claude`
Sleep 1200ms
Enter

# Scope: Project (the default first option).
Wait+Screen@30s /Installation scope/
Sleep 700ms
Enter

# Install method: Symlink (the default, recommended). Appears because the selected
# agents resolve to more than one skills directory.
Wait+Screen@30s /Installation method/
Sleep 700ms
Enter

# Confirm.
Wait+Screen@30s /Install 2 skill/
Sleep 800ms
Enter

# Result.
Wait+Screen@30s /Done/
Sleep 1800ms
Screenshot add-flow-final.png
Sleep 600ms
