# Keyboard input test script for cube movement and action playback

$ErrorActionPreference = "Stop"

function Cleanup {
    uloop control-play-mode --action Stop 2>$null
}

function Press {
    param(
        [string]$Key,
        [float]$Duration = 0.5
    )
    uloop simulate-keyboard --action Press --key $Key --duration $Duration
}

function HoldFor {
    param(
        [float]$Duration,
        [string[]]$Keys
    )
    foreach ($k in $Keys) {
        uloop simulate-keyboard --action KeyDown --key $k
    }
    Start-Sleep -Seconds $Duration
    foreach ($k in $Keys) {
        uloop simulate-keyboard --action KeyUp --key $k
    }
}

function Hold {
    param([string]$Key)
    uloop simulate-keyboard --action KeyDown --key $Key
}

function Release {
    param([string]$Key)
    uloop simulate-keyboard --action KeyUp --key $Key
}

try {
    Write-Host "=== Keyboard Cube Input Test ==="

    uloop control-play-mode --action Play
    Start-Sleep -Seconds 2
    # uloop focus-window
    # Start-Sleep -Seconds 0.5

    # Idle jump
    Press -Key Space -Duration 0.3
    Start-Sleep -Seconds 2.0

    # Walk circle with jump mid-walk
    Hold -Key LeftShift
    Hold -Key W
    Start-Sleep -Seconds 1.0
    Press -Key Space -Duration 0.3
    Start-Sleep -Seconds 2.0
    Release -Key W
    HoldFor -Duration 1.0 -Keys D
    HoldFor -Duration 1.0 -Keys S
    HoldFor -Duration 1.0 -Keys A
    Release -Key LeftShift
    Start-Sleep -Seconds 0.3

    # Run circle with jump mid-run
    Hold -Key W
    Start-Sleep -Seconds 0.8
    Press -Key Space -Duration 0.3
    Start-Sleep -Seconds 2.0
    Release -Key W
    HoldFor -Duration 0.8 -Keys RightArrow
    HoldFor -Duration 0.8 -Keys DownArrow
    HoldFor -Duration 0.8 -Keys LeftArrow
    Start-Sleep -Seconds 0.3

    # Backspace and Delete overlay test
    Press -Key Backspace -Duration 0.3
    Start-Sleep -Seconds 1
    Press -Key Delete -Duration 0.3
    Start-Sleep -Seconds 1

    # Final idle jump
    Press -Key Enter -Duration 0.1
    Start-Sleep -Milliseconds 10
    Press -Key Enter -Duration 0.1
    Start-Sleep -Milliseconds 10
    Press -Key Enter -Duration 0.1
    Start-Sleep -Milliseconds 10
    Press -Key Enter -Duration 0.1
    Start-Sleep -Milliseconds 10
    Press -Key Enter -Duration 0.1
    Start-Sleep -Milliseconds 10
    Press -Key Enter -Duration 0.1
    Start-Sleep -Seconds 5

    Write-Host "=== Keyboard Input Test Complete! ==="
}
finally {
    Cleanup
}
