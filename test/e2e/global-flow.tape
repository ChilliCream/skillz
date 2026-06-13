# End-to-end recording of `skillz add -g` (global scope install).
#
# Non-interactive (--agent + --skill + -y skip every prompt; -g skips the scope
# prompt). Validates the global install path — skills land under $HOME rather
# than the project. HOME is pinned to /tmp/work so the rendered paths are
# deterministic, and a single skill is pinned for a byte-stable report.
#
# Produces global-flow.gif (demo) + global-flow.txt (final-frame golden source).

Output global-flow.gif
Output global-flow.txt

Set Shell bash
Set FontSize 18
Set Width 1100
Set Height 850
Set Padding 14
Set TypingSpeed 50ms
Set CursorBlink false

# --- setup (hidden) ---
# Pin HOME so the global skills directory ($HOME/.claude/skills) renders a fixed
# path; work in the same throwaway dir so nothing escapes /tmp/work.
Hide
Type `export PS1="$ "` Enter
Type `export PATH=/src/test/e2e/bin:$PATH` Enter
Type `cd /tmp && rm -rf work && mkdir work && cd work` Enter
Type `export HOME=/tmp/work` Enter
Type `cp -r /src/test/fixtures/sample-skills .` Enter
Type `clear` Enter
Show

# --- the demo ---
Type `skillz add ./sample-skills --agent claude-code --global --skill commit-helper -y` Enter

Wait+Screen@30s /Done/
Sleep 1800ms
Screenshot global-flow-final.png
Sleep 500ms
