# End-to-end recording of `skillz init` (scaffold a new skill).
#
# Non-interactive: writes <name>/SKILL.md and prints the next-steps summary.
# Produces init-flow.gif (demo) + init-flow.txt (final-frame golden source).

Output init-flow.gif
Output init-flow.txt

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
Type `skillz init my-skill` Enter

Wait+Screen@30s /Initialized skill/
Sleep 1500ms
Screenshot init-flow-final.png
Sleep 500ms
