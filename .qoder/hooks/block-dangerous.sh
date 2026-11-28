#!/bin/bash
# PreToolUse hook (Bash): block dangerous commands.
# Exit 2 = block the tool call; exit 0 = allow.

input=$(cat)

# Locate jq: PATH first, then WinGet user-links fallback (env-var based, portable).
if command -v jq >/dev/null 2>&1; then
  JQ="jq"
elif [ -x "$LOCALAPPDATA/Microsoft/WinGet/Links/jq.exe" ]; then
  JQ="$LOCALAPPDATA/Microsoft/WinGet/Links/jq.exe"
elif [ -x "$HOME/AppData/Local/Microsoft/WinGet/Links/jq.exe" ]; then
  JQ="$HOME/AppData/Local/Microsoft/WinGet/Links/jq.exe"
else
  JQ=""
fi

if [ -n "$JQ" ]; then
  command=$(echo "$input" | "$JQ" -r '.tool_input.command // ""' 2>/dev/null)
else
  # Crude fallback: pull the "command":"..." value straight out of the JSON.
  command=$(echo "$input" | grep -o '"command"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed 's/^.*:[[:space:]]*"//; s/"$//')
fi

if echo "$command" | grep -qiE '7z\.exe|7z\.dll|curl|wget|rm -rf|sudo|chmod 777|del /|rd /s'; then
  echo "高危命令已被阻止，需人工执行: $command" >&2
  exit 2
fi
exit 0
