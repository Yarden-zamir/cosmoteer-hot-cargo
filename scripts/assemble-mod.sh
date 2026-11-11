#!/usr/bin/env bash
# Assemble the rules mod folder from mod/ into $1.
# - mod.rules = mod/mod.header.rules + an Actions list made of every mod/<area>/actions.rules.
# - strings/en.rules = mod/strings/en.rules + every mod/<area>/strings.en.rules.
# Areas own separate files, so parallel work never edits the same file.
# Each actions.rules holds only Action objects { ... }, without an outer list.
set -euo pipefail
SRC="$(cd "$(dirname "$0")/../mod" && pwd)"
OUT="$1"
mkdir -p "$OUT"
rsync -a --exclude 'mod.header.rules' --exclude 'actions.rules' --exclude 'strings.en.rules' "$SRC/" "$OUT/"
{
  cat "$SRC/mod.header.rules"
  echo
  echo "Actions"
  echo "["
  find "$SRC" -mindepth 2 -name actions.rules | sort | while read -r f; do
    echo "	// ---- ${f#$SRC/}"
    cat "$f"
    echo
  done
  echo "]"
} > "$OUT/mod.rules"
{
  cat "$SRC/strings/en.rules"
  find "$SRC" -mindepth 2 -name strings.en.rules | sort | while read -r f; do
    echo "// ---- ${f#$SRC/}"
    cat "$f"
    echo
  done
} > "$OUT/strings/en.rules"
