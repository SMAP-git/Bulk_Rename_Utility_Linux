#!/usr/bin/env bash
set -euo pipefail

VNC_DISPLAY="${VNC_DISPLAY:-:1}"
VNC_GEOMETRY="${VNC_GEOMETRY:-1440x900}"
VNC_PORT="${VNC_PORT:-5902}"
NOVNC_PORT="${NOVNC_PORT:-6081}"
VNC_ARGS=(
    "$VNC_DISPLAY"
    -geometry "$VNC_GEOMETRY"
    -depth 24
    -rfbport "$VNC_PORT"
    -localhost no
)

# A forced container stop can leave the fixed X display socket and lock behind.
rm -f /tmp/.X1-lock /tmp/.X11-unix/X1

mkdir -p "$HOME/.vnc"
if [[ -n "${VNC_PASSWORD:-}" ]]; then
    if (( ${#VNC_PASSWORD} < 6 || ${#VNC_PASSWORD} > 8 )); then
        printf 'VNC_PASSWORD must be 6 to 8 characters.\n' >&2
        exit 1
    fi
    printf '%s\n' "$VNC_PASSWORD" | tigervncpasswd -f > "$HOME/.vnc/passwd"
    chmod 0600 "$HOME/.vnc/passwd"
    VNC_ARGS+=(-SecurityTypes VncAuth -rfbauth "$HOME/.vnc/passwd")
else
    VNC_ARGS+=(-SecurityTypes None)
    printf 'Warning: VNC authentication is disabled. Restrict access to trusted networks.\n' >&2
fi

Xtigervnc "${VNC_ARGS[@]}" &
VNC_PID=$!
openbox --display "$VNC_DISPLAY" &
WM_PID=$!
websockify --web=/usr/share/novnc/ "$NOVNC_PORT" "127.0.0.1:$VNC_PORT" &
NOVNC_PID=$!
cleanup() {
    kill "$NOVNC_PID" "$WM_PID" "$VNC_PID" 2>/dev/null || true
}
trap cleanup EXIT

for attempt in {1..30}; do
    if xdpyinfo -display "$VNC_DISPLAY" >/dev/null 2>&1; then
        break
    fi
    if ! kill -0 "$VNC_PID" 2>/dev/null; then
        wait "$VNC_PID"
    fi
    sleep 1
done
if ! xdpyinfo -display "$VNC_DISPLAY" >/dev/null 2>&1; then
    printf 'VNC display %s did not become ready.\n' "$VNC_DISPLAY" >&2
    exit 1
fi

DISPLAY="$VNC_DISPLAY" BulkRenameUtility &
APP_PID=$!
wait "$APP_PID"
