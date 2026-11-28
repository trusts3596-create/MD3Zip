#!/bin/bash
# PreToolUse hook (Write|Edit): protect read-only paths, allow only workspace config/work dirs.
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
  file_path=$(echo "$input" | "$JQ" -r '.tool_input.file_path // ""' 2>/dev/null)
else
  # Crude fallback: pull the "file_path":"..." value straight out of the JSON.
  file_path=$(echo "$input" | grep -o '"file_path"[[:space:]]*:[[:space:]]*"[^"]*"' | head -1 | sed 's/^.*:[[:space:]]*"//; s/"$//')
fi

norm=$(echo "$file_path" | tr '\\' '/')

# 禁止写入源码目录（配置备份由 git/bash 通道完成，不走 Write/Edit）
if echo "$norm" | grep -qi 'yuanma'; then
  echo "源码目录只读，禁止写入: $file_path" >&2
  exit 2
fi

# 禁止写入内核目录
if echo "$norm" | grep -qiE 'Assets/7z/'; then
  echo "禁止写入内核目录: $file_path" >&2
  exit 2
fi

# 只允许写入工作区内的指定目录
if echo "$norm" | grep -qiE '(\.qoder/|work/|\.gitignore|\.gitattributes)'; then
  exit 0
fi

echo "越界写入被阻止，仅允许 .qoder/ 或 work/ 下: $file_path" >&2
exit 2
