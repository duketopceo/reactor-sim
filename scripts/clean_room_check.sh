#!/usr/bin/env bash
# Clean-room check: fails if any tracked file (its path or its content) mentions a banned term.
#
# The denylist covers third-party product/studio names, internal identifiers of other simulators, reverse-engineering
# tooling, and machine-specific paths. Terms are stored base64-encoded so this file does not trip on itself; this
# file is the only one excluded from the tree scan. Every other tracked file is scanned, license texts included.
#
# What is scanned, per tracked file:
#   * the file path itself
#   * the file bytes, ASCII, case-insensitive, fixed strings (binary assets too: FBX/DLL/PDB embed build paths)
#   * the same bytes with NUL bytes removed, which exposes UTF-16 (LE or BE) text such as .NET resources
#   * compressed content: zstd, gzip, xz, bzip2 and zip data are decompressed and scanned too (a .blend is
#     zstd-compressed and carries UI state; nested layers are followed). If a needed decompressor is missing the
#     check fails closed instead of skipping the file.
#   * separator-tolerant forms of each alphabetic term (single letters separated by 1-3 non-alphanumeric characters,
#     e.g. "f o o b a r", "f-o-o-...")
#   * per-user profile paths in general (drive-letter ...\Users\<name>, /Users/<name>/), not just one user's name.
#
#   bash scripts/clean_room_check.sh               scan the tracked tree
#   bash scripts/clean_room_check.sh --root DIR    scan every file under DIR (used by the self-test)
#   bash scripts/clean_room_check.sh --self-test   negative controls: the scanner must flag compressed, UTF-16,
#                                                  separated, path-only and profile-path fixtures, and pass clean ones
set -euo pipefail
cd "$(dirname "$0")/.."
SELF="$(pwd)/scripts/clean_room_check.sh"

DENY_B64='bnVjbGVhcmVz
Y251Y2xlbw==
Y2NvbWJ1c3Q=
aW50ZXJjYW1iaWFkb3I=
dmVsX2Nhcmdh
cG90ZW5jaWE=
ZG9zc2llcg==
ZGVjb21waWw=
ZG5zcHk=
aWxzcHk=
cmVmZXJlbmNlIGdhbWU=
cmVmZXJlbmNlIGltcGxlbWVudGF0aW9u
cmVmZXJlbmNlIHNpbQ==
Y3liZXJwb3dh
a2ltYmE=
L2hvbWUv
YzpcdXNlcnM=
c3RlYW1wb3dlcmVk'

MODE=tree
ROOT=""
case "${1:-}" in
  "") ;;
  --self-test) MODE=selftest ;;
  --root) MODE=root; ROOT="${2:?--root needs a directory}" ;;
  *) echo "usage: $0 [--root DIR | --self-test]" >&2; exit 2 ;;
esac

# the local user-name token, kept encoded like the rest of the denylist
USER_TOKEN="$(printf '%s' 'a2ltYmE=' | base64 -d)"
patterns=()      # every denylist term (for the report count)
fixed=()         # terms matched as plain substrings
extra_re=()      # terms that need a regex instead
while IFS= read -r line; do
  [ -n "$line" ] || continue
  t="$(printf '%s' "$line" | base64 -d)"
  patterns+=("$t")
  case "$t" in
    # a local user name: must not match the common surname that starts with the same letters
    "$USER_TOKEN") extra_re+=("${USER_TOKEN}([^l]|\$)") ;;
    # Windows profile root: covered by the generic profile-path regexes below, which allow <placeholders>
    'c:\users') ;;
    *) fixed+=("$t") ;;
  esac
done <<< "$DENY_B64"

WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
pf="$WORK/fixed.txt"   # fixed-string patterns
rf="$WORK/regex.txt"   # extended regexes
printf '%s\n' "${fixed[@]}" > "$pf"

# Separator-tolerant regex for every purely alphabetic term of 5+ letters.
: > "$rf"
for r in "${extra_re[@]}"; do printf '%s\n' "$r" >> "$rf"; done
for p in "${patterns[@]}"; do
  if [[ "$p" =~ ^[A-Za-z]{5,}$ ]]; then
    re=""
    for ((i = 0; i < ${#p}; i++)); do
      [ -n "$re" ] && re+='[^a-z0-9]{1,3}'
      re+="${p:i:1}"
    done
    printf '%s\n' "$re" >> "$rf"
  fi
done
# Per-user profile paths: "C:\Users\name", "C:\\Users\\name", "d:/users/name", "/Users/name/".
# Placeholders (<name>, {name}, $NAME, %NAME%, [name]) are allowed.
printf '%s\n' '[a-z]:[\\/]+users[\\/]+[^<>{}$%[:space:]"'"'"'*\\/]' >> "$rf"
printf '%s\n' '(^|[^a-z0-9._-])/users/[a-z0-9._-]+/' >> "$rf"

# Print the compression kind of a file from its magic bytes, or nothing.
magic_kind() {
  local hex
  hex="$(head -c 4 "$1" 2>/dev/null | od -An -tx1 | tr -d ' \n')"
  case "$hex" in
    28b52ffd*) echo zstd ;;
    1f8b*)     echo gzip ;;
    fd377a58*) echo xz ;;
    425a68*)   echo bzip2 ;;
    504b0304*) echo zip ;;
  esac
}

have() { command -v "$1" >/dev/null 2>&1; }

# zstd may be a native binary, or (Git Bash on Windows) the one inside WSL; stdin/stdout streaming works for both.
ZSTD_MODE=""
zstd_mode() {
  if [ -z "$ZSTD_MODE" ]; then
    if have zstd; then ZSTD_MODE=native
    elif have wsl.exe && wsl.exe -e sh -c 'command -v zstd' >/dev/null 2>&1; then ZSTD_MODE=wsl
    else ZSTD_MODE=none; fi
  fi
  [ "$ZSTD_MODE" != none ]
}
zstd_cmd() { if [ "$ZSTD_MODE" = wsl ]; then wsl.exe -e zstd "$@"; else zstd "$@"; fi; }

# decompress KIND IN OUT ; returns non-zero if it cannot (missing tool or corrupt data).
decompress() {
  local kind="$1" in="$2" out="$3"
  case "$kind" in
    zstd)
      if zstd_mode; then zstd_cmd -dc < "$in" > "$out" 2>/dev/null
      elif have python3 && python3 -c 'import compression.zstd' 2>/dev/null; then
        python3 -I -c 'import sys,compression.zstd as z; sys.stdout.buffer.write(z.decompress(open(sys.argv[1],"rb").read()))' "$in" > "$out"
      else return 1; fi ;;
    gzip)  have gzip  && gzip -dc -- "$in" > "$out" 2>/dev/null ;;
    xz)    have xz    && xz -dc -- "$in" > "$out" 2>/dev/null ;;
    bzip2) have bzip2 && bzip2 -dc -- "$in" > "$out" 2>/dev/null ;;
    zip)
      if have python3; then
        python3 -I -c 'import sys,zipfile
z=zipfile.ZipFile(sys.argv[1])
for n in z.namelist(): sys.stdout.buffer.write(n.encode()+b"\n"+z.read(n)+b"\n")' "$in" > "$out" 2>/dev/null
      elif have unzip; then unzip -p -- "$in" > "$out" 2>/dev/null
      else return 1; fi ;;
    *) return 1 ;;
  esac
}

# scan_view FILE : prints hit lines for one byte stream and returns 0 if there was a hit.
scan_view() {
  local v="$1" hit="" nul="$WORK/nul.bin"
  hit="$(grep -a -n -i -F -f "$pf" -- "$v" 2>/dev/null | tr -d '\0' | cut -c1-160 | head -3 || true)"
  if [ -z "$hit" ]; then
    hit="$(grep -a -n -i -E -f "$rf" -- "$v" 2>/dev/null | tr -d '\0' | cut -c1-160 | head -3 || true)"
  fi
  if [ -z "$hit" ]; then
    # NUL-stripped view: UTF-16 text becomes plain ASCII here.
    tr -d '\0' < "$v" > "$nul"
    if ! cmp -s "$v" "$nul"; then
      hit="$(grep -a -n -i -F -f "$pf" -- "$nul" 2>/dev/null | cut -c1-160 | head -3 || true)"
      if [ -z "$hit" ]; then hit="$(grep -a -n -i -E -f "$rf" -- "$nul" 2>/dev/null | cut -c1-160 | head -3 || true)"; fi
      if [ -n "$hit" ]; then hit="(UTF-16 or NUL-separated) $hit"; fi
    fi
  fi
  if [ -n "$hit" ]; then printf '%s\n' "$hit"; return 0; fi
  return 1
}

# scan_file FILE LABEL : follows nested compression up to 4 layers; returns 0 on a finding (after printing it).
scan_file() {
  local cur="$1" label="$2" depth=0 kind out hit note
  while :; do
    note=""
    if [ "$depth" -gt 0 ]; then note=" (inside $depth compressed layer(s))"; fi
    if hit="$(scan_view "$cur")"; then
      printf 'BANNED TERM in %s%s\n%s\n' "$label" "$note" "$hit" >&2
      return 0
    fi
    kind="$(magic_kind "$cur")"
    if [ -z "$kind" ]; then return 1; fi
    depth=$((depth + 1))
    if [ "$depth" -gt 4 ]; then
      printf 'UNSCANNABLE %s: more than 4 nested compressed layers\n' "$label" >&2
      return 0
    fi
    out="$WORK/layer$depth.bin"
    if ! decompress "$kind" "$cur" "$out"; then
      printf 'UNSCANNABLE %s: cannot decompress %s data (install the %s tool); failing closed\n' "$label" "$kind" "$kind" >&2
      return 0
    fi
    cur="$out"
  done
}

# scan_path LABEL : the file path text itself.
scan_path() {
  local label="$1"
  if printf '%s\n' "$label" | grep -i -F -q -f "$pf" || printf '%s\n' "$label" | grep -i -E -q -f "$rf"; then
    printf 'BANNED TERM in path: %s\n' "$label" >&2
    return 0
  fi
  return 1
}

# run_scan BASEDIR : NUL-separated relative file list on stdin; prints the finding count.
run_scan() {
  local base="$1" f hits=0
  while IFS= read -r -d '' f; do
    if [ "$MODE" = tree ] && [ "$f" = "scripts/clean_room_check.sh" ]; then continue; fi
    [ -f "$base/$f" ] || continue
    if scan_path "$f"; then hits=$((hits + 1)); fi
    if scan_file "$base/$f" "$f"; then hits=$((hits + 1)); fi
  done
  echo "$hits"
}

self_test() {
  local t="$WORK/selftest" fails=0 term hello out c
  term="$(printf '%s' 'bnVjbGVhcmVz' | base64 -d)"
  mkdir -p "$t"
  mk() { mkdir -p "$t/$1"; }
  expect_hit() {   # the scanner must exit non-zero for this fixture root
    if out="$("$SELF" --root "$t/$1" 2>&1)"; then
      echo "SELF-TEST FAIL: $1 was not detected"; fails=$((fails + 1))
    else
      echo "self-test ok: $1 detected"
    fi
  }
  expect_clean() {
    if out="$("$SELF" --root "$t/$1" 2>&1)"; then echo "self-test ok: $1 passes"
    else echo "SELF-TEST FAIL: $1 should pass: $out"; fails=$((fails + 1)); fi
  }
  hello="prefix padding $term trailing padding"

  mk plain;   printf '%s\n' "$hello" > "$t/plain/a.txt"
  mk gzip;    printf '%s\n' "$hello" | gzip -c > "$t/gzip/a.bin"
  mk xz;      printf '%s\n' "$hello" | xz -c > "$t/xz/a.bin"
  mk utf16le; printf '%s' "$hello" | iconv -f UTF-8 -t UTF-16LE > "$t/utf16le/a.bin"
  mk utf16be; printf '%s' "$hello" | iconv -f UTF-8 -t UTF-16BE > "$t/utf16be/a.bin"
  mk spaced;  printf 'x %s y\n' "$(printf '%s' "$term" | sed 's/./& /g')" > "$t/spaced/a.txt"
  mk dashed;  printf 'x %s y\n' "$(printf '%s' "$term" | sed 's/./&-/g')" > "$t/dashed/a.txt"
  mk pathonly; : > "$t/pathonly/notes-$term.txt"
  mk profile; printf 'saved to %s\n' 'C:\Users\someone\Documents\x.blend' > "$t/profile/a.txt"
  mk profile2; printf 'saved to %s\n' 'C:\\Users\\someone\\x' > "$t/profile2/a.json"
  mk macprofile; printf 'open %s\n' '/Users/someone/work/x' > "$t/macprofile/a.txt"
  mk licenseglob; printf '%s\n' "$hello" > "$t/licenseglob/NOTALICENSE"
  mk nested; printf '%s\n' "$hello" | gzip -c | xz -c > "$t/nested/a.bin"
  mk clean;   printf 'a perfectly ordinary file about a reactor\n' > "$t/clean/a.txt"
  mk placeholder; printf 'path C:\\Users\\<you>\\x and /Users/<you>/x and users/operators/groups\n' > "$t/placeholder/a.txt"
  mk cleanzip; printf 'ordinary\n' | gzip -c > "$t/cleanzip/a.bin"

  if zstd_mode; then
    mk zstd; printf '%s\n' "$hello" | zstd_cmd -q -c > "$t/zstd/a.blend"
    mk zstdutf16; printf '%s' "$hello" | iconv -f UTF-8 -t UTF-16LE | zstd_cmd -q -c > "$t/zstdutf16/a.blend"
    mk zstdgz; printf '%s\n' "$hello" | gzip -c | zstd_cmd -q -c > "$t/zstdgz/a.blend"
    mk cleanzstd; printf 'ordinary\n' | zstd_cmd -q -c > "$t/cleanzstd/a.blend"
    for c in zstd zstdutf16 zstdgz; do expect_hit "$c"; done
    expect_clean cleanzstd
  else
    echo "SELF-TEST FAIL: no zstd tool available to build the compressed fixtures, cannot prove compressed-content detection"
    fails=$((fails + 1))
  fi
  for c in plain gzip xz utf16le utf16be spaced dashed pathonly profile profile2 macprofile licenseglob nested; do expect_hit "$c"; done
  for c in clean placeholder cleanzip; do expect_clean "$c"; done

  if [ "$fails" -gt 0 ]; then echo "clean-room self-test FAILED: $fails case(s)" >&2; return 1; fi
  echo "clean-room self-test passed (all negative controls detected, clean fixtures pass)."
}

if [ "$MODE" = selftest ]; then self_test; exit $?; fi

if [ "$MODE" = root ]; then
  hits="$(cd "$ROOT" && find . -type f -print0 | sed -z 's|^\./||' | run_scan "$(pwd)")"
else
  hits="$(git ls-files -z | run_scan "$(pwd)")"
fi

if [ "${hits:-0}" -gt 0 ]; then
  echo "clean-room check FAILED: $hits finding(s)." >&2
  exit 1
fi
echo "clean-room check passed (${#patterns[@]} terms; ASCII, UTF-16, separator-tolerant and compressed-content scans; no hits)."
