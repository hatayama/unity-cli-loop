#!/bin/sh
# Keyboard input test script for cube movement and action playback

set -e

cleanup() {
    uloop control-play-mode --action Stop 2>/dev/null
}
trap cleanup EXIT INT TERM

press() {
    uloop simulate-keyboard --action Press --key "$1" --duration "${2:-0.5}"
}

hold_for() {
    duration="$1"
    shift
    for key in "$@"; do
        uloop simulate-keyboard --action KeyDown --key "$key"
    done
    sleep "$duration"
    for key in "$@"; do
        uloop simulate-keyboard --action KeyUp --key "$key"
    done
}

hold() {
    uloop simulate-keyboard --action KeyDown --key "$1"
}

release() {
    uloop simulate-keyboard --action KeyUp --key "$1"
}

echo "=== Keyboard Cube Input Test ==="

uloop control-play-mode --action Play
sleep 2
# uloop focus-window
# sleep 0.5

# Idle jump
press Space 0.3
sleep 2.0

# Walk circle with jump mid-walk
hold LeftShift
hold W
sleep 1.0
press Space 0.3
sleep 2.0
release W
hold_for 1.0 D
hold_for 1.0 S
hold_for 1.0 A
release LeftShift
sleep 0.3

# Run circle with jump mid-run
hold W
sleep 0.8
press Space 0.3
sleep 2.0
release W
hold_for 0.8 RightArrow
hold_for 0.8 DownArrow
hold_for 0.8 LeftArrow
sleep 0.3

# Backspace and Delete overlay test
press Backspace 0.3
sleep 1
press Delete 0.3
sleep 1

# Final idle jump
press Enter 0.1
sleep 0.01
press Enter 0.1
sleep 0.01
press Enter 0.1
sleep 0.01
press Enter 0.1
sleep 0.01
press Enter 0.1
sleep 0.01
press Enter 0.1
sleep 5

echo "=== Keyboard Input Test Complete! ==="
