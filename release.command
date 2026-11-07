#!/usr/bin/env bash
# Double-click it (macOS Finder; a Linux file manager: "Run as a program") to build every package
# into dist/ with ./release.sh: the gate, the three archives, the Windows setup when NSIS is
# installed, SHA256SUMS. Opens dist/ when it worked and waits for Enter, so the window stays
# readable. Options go to release.sh (--no-check, --no-setup). Windows: release.bat.
# D3SK_UNATTENDED=1: no wait, no folder opened (scripted runs).
cd "$(dirname "$0")" || exit 1
# A double-click starts a bare shell: PATH may miss dotnet.
if ! command -v dotnet >/dev/null 2>&1; then
  for d in "${DOTNET_ROOT:-}" /usr/local/share/dotnet /opt/dotnet "$HOME/.dotnet"; do
    if [ -n "$d" ] && [ -x "$d/dotnet" ]; then export PATH="$d:$PATH"; break; fi
  done
fi

./release.sh "$@"
code=$?
echo
if [ "$code" -eq 0 ]; then
  echo "Packages are in $(pwd)/dist"
  if [ -z "${D3SK_UNATTENDED:-}" ]; then
    if [ "$(uname)" = Darwin ]; then open dist
    elif command -v xdg-open >/dev/null 2>&1 && [ -n "${DISPLAY:-}${WAYLAND_DISPLAY:-}" ]; then
      xdg-open dist >/dev/null 2>&1 &
    fi
  fi
else
  echo "release.sh failed (exit code $code): see the messages above." >&2
fi
if [ -z "${D3SK_UNATTENDED:-}" ] && [ -t 0 ]; then read -r -p "Press Enter to close. " _; fi
exit "$code"
