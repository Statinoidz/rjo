# StatinoidFPS.ps1
# PowerShell port of the Bash pipeline: strobe → BJH → voxel → handshake → banner → XY

# ----------------------------------------
#   GLOBAL STATE
# ----------------------------------------
$global:Colors = @(
    'White','DarkGray','DarkGreen','Yellow','Green','DarkGreen',
    'Cyan','DarkCyan','Blue','DarkBlue','Magenta','DarkMagenta','Red','DarkRed'
)
$global:FpsLast      = Get-Date
$global:FpsValue     = 0
$global:VoxelX       = 1
$global:VoxelY       = 1
$global:ChirpEnabled = $false
$global:LastChirpMs  = 0

$global:BannerLines = @(
"███████╗████████╗ █████╗ ████████╗██╗███╗   ██╗ ██████╗ ██╗█████╗ █████████╗"
"██╔════╝╚██╔═██╔╝██╔══██╗╚██╔═██╔╝██║███║   ██║██╔═══██╗██║██╔═██╗╚═════██═╝"
"███████╗ ██║ ██║ ███████║ ██║ ██║ ██║██╔██╗ ██║██║   ██║██║██║  ██    ██═╝"
"╚════██║ ██║ ██║ ██╔══██║ ██║ ██║ ██║██║╚██╗██║██║   ██║██║██║ ██╝  ██═╝"
"███████║██╔╝ ██║ ██║  ██║██╔╝ ██║ ██║██║ ╚████║╚██████╔╝██║█████╝ ████████╗"
"╚══════╝╚═╝  ╚═╝ ╚═╝  ╚═╝╚═╝  ╚═╝ ╚═╝╚═╝  ╚═══╝ ╚═════╝ ╚═╝╚═══╝  ╚═══════╝"
)
$global:AnchorLine = "Statinoidz______________________________________________________/"

# ----------------------------------------
#   ASCII DIGITS FOR XY PIPELINE
# ----------------------------------------
$global:Digits = @{
    '0' = @(
        "███"
        "█ █"
        "█ █"
        "█ █"
        "█ █"
        "█ █"
        "███"
    )
    '1' = @(
        " ██"
        "███"
        " ██"
        " ██"
        " ██"
        " ██"
        "████"
    )
    '2' = @(
        "███"
        "  █"
        "  █"
        "███"
        "█  "
        "█  "
        "███"
    )
    '3' = @(
        "███"
        "  █"
        "  █"
        "███"
        "  █"
        "  █"
        "███"
    )
    '4' = @(
        "█ █"
        "█ █"
        "█ █"
        "███"
        "  █"
        "  █"
        "  █"
    )
    '5' = @(
        "███"
        "█  "
        "█  "
        "███"
        "  █"
        "  █"
        "███"
    )
    '6' = @(
        "███"
        "█  "
        "█  "
        "███"
        "█ █"
        "█ █"
        "███"
    )
    '7' = @(
        "███"
        "  █"
        "  █"
        "  █"
        "  █"
        "  █"
        "  █"
    )
    '8' = @(
        "███"
        "█ █"
        "█ █"
        "███"
        "█ █"
        "█ █"
        "███"
    )
    '9' = @(
        "███"
        "█ █"
        "█ █"
        "███"
        "  █"
        "  █"
        "███"
    )
}

function Strike-Digit {
    param([string[]]$Glyph)
    $out = @()
    for ($i=0; $i -lt $Glyph.Count; $i++) {
        if ($i -eq 3) {
            $out += "███"
        } else {
            $out += $Glyph[$i]
        }
    }
    return $out
}

# ----------------------------------------
#   UTILS
# ----------------------------------------
function Get-NowMs {
    return [int64]((Get-Date).ToUniversalTime().Subtract([datetime]'1970-01-01').TotalMilliseconds)
}

function Update-Fps {
    $now = Get-Date
    $dt  = ($now - $global:FpsLast).TotalMilliseconds
    if ($dt -le 0) {
        $global:FpsValue = 0
    } else {
        $global:FpsValue = [int](1000.0 / $dt)
    }
    $global:FpsLast = $now
}

function Sleep-Ms {
    param([int]$Ms)
    Start-Sleep -Milliseconds $Ms
}

function Get-WindowSize {
    $raw = $Host.UI.RawUI
    return @{
        Cols  = $raw.WindowSize.Width
        Lines = $raw.WindowSize.Height
    }
}

function Coord {
    param(
        [int]$X,
        [int]$Y
    )
    return New-Object System.Management.Automation.Host.Coordinates -ArgumentList $X, $Y
}

function Safe-SetCursor {
    param(
        [int]$X,
        [int]$Y
    )
    try {
        $Host.UI.RawUI.CursorPosition = Coord $X $Y
    } catch {
        # ignore invalid positions (e.g., during resize)
    }
}

function Clear-ScreenFull {
    Clear-Host
    Safe-SetCursor 0 0
}

function Maybe-Chirp {
    if (-not $global:ChirpEnabled) { return }
    $now = Get-NowMs
    if ($now - $global:LastChirpMs -ge 1000) {
        Play-Tone 880 40
        $global:LastChirpMs = $now
    }
}

# ----------------------------------------
#   SOUND SYSTEM (KEY-BASED TONES)
# ----------------------------------------
function Play-Tone {
    param(
        [int]$Freq,
        [int]$DurMs
    )
    try {
        [Console]::Beep($Freq, $DurMs)
    } catch {
        # ignore if not supported
    }
}

function Play-KeyChirp {
    param([char]$Key)
    switch ($Key) {
        ' ' { Play-Tone 880 60; break }   # space
        'w' { Play-Tone 660 60; break }
        'a' { Play-Tone 550 60; break }
        's' { Play-Tone 440 60; break }
        'd' { Play-Tone 770 60; break }
        'q' { Play-Tone 330 60; break }
        'e' { Play-Tone 990 60; break }
        default { Play-Tone 600 40; break }
    }
}

# ----------------------------------------
#   VOXEL SYSTEM (X + Y)
# ----------------------------------------
function Update-VoxelSizes {
    $ws = Get-WindowSize
    $cols  = $ws.Cols
    $lines = $ws.Lines

    $global:VoxelX = [math]::Max([int]($cols / 16), 1)
    $global:VoxelY = [math]::Max([int]($lines / 16), 1)
}

# ----------------------------------------
#   CHECKERBOARD HELPERS
# ----------------------------------------
function Draw-CheckerboardRegion {
    param(
        [int]$StartRow,
        [int]$EndRow
    )

    $ws = Get-WindowSize
    $cols  = $ws.Cols
    $lines = $ws.Lines
    if ($EndRow -gt $lines) { $EndRow = $lines }

    $raw = $Host.UI.RawUI

    for ($y = $StartRow; $y -le $EndRow; $y++) {
        Safe-SetCursor 0 ($y-1)
        $line = New-Object System.Text.StringBuilder
        for ($x = 0; $x -lt $cols; $x++) {
            $cx = [int]($x / $global:VoxelX)
            $cy = [int]($y / $global:VoxelY)
            if ( (($cx + $cy) % 2) -eq 0 ) {
                [void]$line.Append(" ")
            } else {
                [void]$line.Append(" ")
            }
        }
        if ( ($y % 2) -eq 0 ) {
            $raw.BackgroundColor = 'Black'
        } else {
            $raw.BackgroundColor = 'White'
        }
        $raw.ForegroundColor = 'Black'
        Write-Host $line.ToString() -NoNewline
        Maybe-Chirp
    }
    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = 'Gray'
}

function Draw-CheckerboardBackground {
    $bannerHeight = $global:BannerLines.Count + 2
    $ws = Get-WindowSize
    Draw-CheckerboardRegion -StartRow $bannerHeight -EndRow $ws.Lines
}

# ----------------------------------------
#   BANNER + CHECKERBOARD
# ----------------------------------------
function Show-StatinoidzBanner {
    param([int]$ColorIndex)

    Update-Fps
    Update-VoxelSizes

    $ws = Get-WindowSize
    $cols  = $ws.Cols

    $raw = $Host.UI.RawUI
    Safe-SetCursor 0 0

    $raw.BackgroundColor = 'White'
    $raw.ForegroundColor = 'Black'
    $fpsText = ("FPS: {0}" -f $global:FpsValue)
    Write-Host ($fpsText.PadRight($cols)) -NoNewline

    Safe-SetCursor 0 1

    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = $global:Colors[$ColorIndex % $global:Colors.Count]

    foreach ($line in $global:BannerLines) {
        $padded = $line.PadRight($cols)
        Write-Host $padded -NoNewline
        $pos = $raw.CursorPosition
        Safe-SetCursor 0 ($pos.Y + 1)
    }

    $raw.ForegroundColor = 'Gray'
    $anchor = $global:AnchorLine.PadRight($cols)
    Write-Host $anchor -NoNewline

    Draw-CheckerboardBackground

    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = 'Gray'
}

# ----------------------------------------
#   VISUAL PHASES
# ----------------------------------------
function Run-StrobePhase {
    Clear-ScreenFull
    Update-VoxelSizes
    $ws = Get-WindowSize
    $lines = $ws.Lines
    $cols  = $ws.Cols
    $raw = $Host.UI.RawUI

    for ($i=0; $i -lt 20; $i++) {
        $raw.BackgroundColor = 'White'
        $raw.ForegroundColor = 'Black'
        Safe-SetCursor 0 0
        for ($r=0; $r -lt $lines; $r++) {
            Write-Host ("".PadRight($cols)) -NoNewline
        }
        Maybe-Chirp
        Sleep-Ms 60

        Clear-ScreenFull
        Draw-CheckerboardRegion -StartRow 1 -EndRow $lines
        Sleep-Ms 60
    }

    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = 'Gray'
}

function Get-RandNoise {
    $chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+=-{}[]<>?/.,"
    $sb = New-Object System.Text.StringBuilder
    for ($i=0; $i -lt 32; $i++) {
        $idx = Get-Random -Minimum 0 -Maximum $chars.Length
        [void]$sb.Append($chars[$idx])
    }
    return $sb.ToString()
}

function Run-BjhStrobe {
    Clear-ScreenFull
    Update-VoxelSizes
    $ws = Get-WindowSize
    $cols = $ws.Cols
    $raw = $Host.UI.RawUI

    $raw.ForegroundColor = 'White'
    $raw.BackgroundColor = 'Black'
    Safe-SetCursor 0 0
    Write-Host ("██████████████████████████████████████████████████████████████".PadRight($cols)) -NoNewline
    Safe-SetCursor 0 1
    Write-Host ("██ B ██".PadRight($cols)) -NoNewline
    Safe-SetCursor 0 2
    Write-Host ("██████████████████████████████████████████████████████████████".PadRight($cols)) -NoNewline

    $usedRows = 3
    Draw-CheckerboardRegion -StartRow ($usedRows+1) -EndRow $ws.Lines

    Sleep-Ms 200

    for ($i=0; $i -lt 12; $i++) {
        $noise = Get-RandNoise
        Clear-ScreenFull
        $raw.ForegroundColor = 'Yellow'
        $raw.BackgroundColor = 'Black'
        Safe-SetCursor 0 0
        Write-Host ("██████████████████████████████████████████████████████████████".PadRight($cols)) -NoNewline
        Safe-SetCursor 0 1
        Write-Host (("██ {0} ██" -f $noise).PadRight($cols)) -NoNewline
        Safe-SetCursor 0 2
        Write-Host ("██████████████████████████████████████████████████████████████".PadRight($cols)) -NoNewline

        Draw-CheckerboardRegion -StartRow 4 -EndRow $ws.Lines
        Maybe-Chirp
        Sleep-Ms 80
    }

    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = 'Gray'
}

function Run-VoxelPhase {
    Clear-ScreenFull
    Update-VoxelSizes
    Update-Fps

    $ws = Get-WindowSize
    $cols  = $ws.Cols
    $lines = $ws.Lines
    $raw = $Host.UI.RawUI

    Safe-SetCursor 0 0
    $raw.ForegroundColor = 'DarkGray'
    $raw.BackgroundColor = 'Black'
    $header = ("VOXEL PHASE  vx={0} vy={1}  FPS:{2}" -f $global:VoxelX, $global:VoxelY, $global:FpsValue)
    Write-Host ($header.PadRight($cols)) -NoNewline

    $gx = [math]::Max([int]($cols / $global:VoxelX), 8)
    $gy = [math]::Max([int](($lines - 1) / $global:VoxelY), 4)

    $row = 2
    for ($vy=0; $vy -lt $gy; $vy++) {
        if ($row -ge $lines) { break }
        Safe-SetCursor 0 ($row-1)
        $lineSb = New-Object System.Text.StringBuilder
        for ($vx=0; $vx -lt $gx; $vx++) {
            if ( (($vx + $vy) % 3) -eq 0 ) {
                [void]$lineSb.Append("·")
            } else {
                [void]$lineSb.Append(" ")
            }
        }
        Write-Host $lineSb.ToString() -NoNewline
        $row++
        Maybe-Chirp
    }

    Draw-CheckerboardRegion -StartRow $row -EndRow $lines
    Sleep-Ms 600

    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = 'Gray'
}

function Run-JbjHandshake {
    Clear-ScreenFull
    Update-VoxelSizes
    Update-Fps

    $ws = Get-WindowSize
    $cols  = $ws.Cols
    $lines = $ws.Lines
    $raw = $Host.UI.RawUI

    Safe-SetCursor 0 0
    $raw.ForegroundColor = 'DarkGray'
    $raw.BackgroundColor = 'Black'
    $fpsLine = ("FPS: {0}" -f $global:FpsValue)
    Write-Host ($fpsLine.PadRight($cols)) -NoNewline

    Safe-SetCursor 0 1
    $raw.ForegroundColor = 'Cyan'
    Write-Host ("JBJ HANDSHAKE".PadRight($cols)) -NoNewline

    $colsGrid = [math]::Max([int]($cols / $global:VoxelX / 2), 4)
    $rowsGrid = [math]::Max([int](($lines - 2) / $global:VoxelY / 2), 4)

    $raw.ForegroundColor = 'Yellow'
    for ($r=0; $r -lt $rowsGrid; $r++) {
        if (2 + $r -ge $lines) { break }
        Safe-SetCursor 0 (2 + $r)
        $line = "." * $colsGrid
        Write-Host $line -NoNewline
        Maybe-Chirp
    }

    $usedRows = $rowsGrid + 2
    Draw-CheckerboardRegion -StartRow ($usedRows+1) -EndRow $lines

    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = 'Gray'
}

# ----------------------------------------
#   MAIN TITLE LOOP (AUTO ROLLOVER ~10s)
# ----------------------------------------
function Run-StatinoidzLoop {
    $idx = 0
    Show-StatinoidzBanner -ColorIndex $idx

    $startMs = Get-NowMs

    while ($true) {
        $nowMs = Get-NowMs
        $dt = $nowMs - $startMs
        if ($dt -gt 10000) { break }

        if ([Console]::KeyAvailable) {
            $keyInfo = [Console]::ReadKey($true)
            $keyChar = $keyInfo.KeyChar
            if ($keyInfo.Key -eq 'Escape') { break }

            Play-KeyChirp $keyChar

            if ($keyChar -eq ' ') {
                Update-VoxelSizes
                Show-StatinoidzBanner -ColorIndex $idx
                continue
            }

            $idx = ($idx + 1) % $global:Colors.Count
            Show-StatinoidzBanner -ColorIndex $idx
        }

        Sleep-Ms 200
    }
}

# ----------------------------------------
#   XY RENDERING (FULL-SCREEN BLOCK ASCII + CHECKERBOARD FILL)
# ----------------------------------------
function Render-XyDigits {
    param(
        [int]$X,
        [int]$Y
    )

    $ax = [math]::Abs($X)
    $ay = [math]::Abs($Y)

    $axStr = "{0:00}" -f $ax
    $ayStr = "{0:00}" -f $ay

    $d1 = $axStr[0]
    $d2 = $axStr[1]
    $d3 = $ayStr[0]
    $d4 = $ayStr[1]

    $g1 = $global:Digits["$d1"]
    $g2 = $global:Digits["$d2"]
    $g3 = $global:Digits["$d3"]
    $g4 = $global:Digits["$d4"]

    if ($X -lt 0) {
        $g1 = Strike-Digit $g1
        $g2 = Strike-Digit $g2
    }
    if ($Y -lt 0) {
        $g3 = Strike-Digit $g3
        $g4 = Strike-Digit $g4
    }

    $rows = @()
    for ($i=0; $i -lt 7; $i++) {
        $rows += ($g1[$i] + " " + $g2[$i] + "  " + $g3[$i] + " " + $g4[$i])
    }
    return $rows
}

function Scale-Row {
    param(
        [string]$Text,
        [int]$ScaleX
    )
    if ($ScaleX -le 1) { return $Text }
    $sb = New-Object System.Text.StringBuilder
    foreach ($ch in $Text.ToCharArray()) {
        for ($i=0; $i -lt $ScaleX; $i++) {
            [void]$sb.Append($ch)
        }
    }
    return $sb.ToString()
}

function Get-XyFromBlob {
    $candidates = @()
    if ($PSScriptRoot) {
        $candidates += (Join-Path $PSScriptRoot "StatiBlob.txt")
    }
    $candidates += (Join-Path (Get-Location).Path "StatiBlob.txt")
    if ($env:USERPROFILE) {
        $candidates += (Join-Path $env:USERPROFILE "StatiBlob.txt")
    }

    foreach ($path in $candidates) {
        if (Test-Path $path) {
            try {
                $content = Get-Content $path -ErrorAction Stop | Select-Object -First 1
                if (-not $content) { continue }
                $parts = $content -split '[,; ]+' | Where-Object { $_ -ne "" }
                if ($parts.Count -ge 2) {
                    $x = [int]$parts[0]
                    $y = [int]$parts[1]
                    return @{ X = $x; Y = $y }
                }
            } catch {
                continue
            }
        }
    }
    return $null
}

function Render-XyFullscreen {
    param(
        [int]$X,
        [int]$Y
    )

    $ws = Get-WindowSize
    $cols  = $ws.Cols
    $lines = $ws.Lines
    $raw = $Host.UI.RawUI

    Update-Fps
    Clear-ScreenFull

    Safe-SetCursor 0 0
    $raw.BackgroundColor = 'White'
    $raw.ForegroundColor = 'Black'
    $status = ("FPS:{0,3}  X:{1,4}  Y:{2,4}" -f $global:FpsValue, $X, $Y)
    Write-Host ($status.PadRight($cols)) -NoNewline

    $glyph = Render-XyDigits -X $X -Y $Y

    $glyphRows   = 7
    $topMargin   = 2
    $usableLines = [math]::Max($lines - $topMargin, $glyphRows)
    $scaleY      = [math]::Max([int]($usableLines / $glyphRows), 1)
    $padTop      = [int](($usableLines - $glyphRows * $scaleY) / 2)

    $currentRow = $topMargin + $padTop

    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = 'Gray'

    $baseRow = $glyph[0]
    $baseLen = $baseRow.Length
    if ($baseLen -lt 1) { $baseLen = 1 }
    $scaleX = [math]::Max([int]($cols / $baseLen), 1)

    foreach ($rowText in $glyph) {
        $scaledRow = Scale-Row -Text $rowText -ScaleX $scaleX
        for ($k=0; $k -lt $scaleY; $k++) {
            if ($currentRow -ge $lines) { break }
            $len = $scaledRow.Length
            if ($len -gt $cols) {
                $lineOut = $scaledRow.Substring(0, $cols)
            } else {
                $pad = [math]::Max([int](($cols - $len) / 2), 0)
                $lineOut = (("".PadLeft($pad) + $scaledRow).PadRight($cols))
            }
            Safe-SetCursor 0 ($currentRow)
            Write-Host $lineOut -NoNewline
            $currentRow++
        }
    }

    Draw-CheckerboardRegion -StartRow ($currentRow+1) -EndRow $lines

    $raw.BackgroundColor = 'Black'
    $raw.ForegroundColor = 'Gray'
}

function Run-XyPipeline {
    while ($true) {
        $blob = Get-XyFromBlob
        if ($blob -ne $null) {
            $X = $blob.X
            $Y = $blob.Y
        } else {
            $X = (Get-Random -Minimum -99 -Maximum 100)
            $Y = (Get-Random -Minimum -99 -Maximum 100)
        }

        Render-XyFullscreen -X $X -Y $Y

        if ([Console]::KeyAvailable) {
            $keyInfo = [Console]::ReadKey($true)
            if ($keyInfo.Key -eq 'Escape') { break }
            Play-KeyChirp $keyInfo.KeyChar
        }

        Sleep-Ms 80
    }
}

# ----------------------------------------
#   MAIN LOOP
# ----------------------------------------
while ($true) {
    $global:ChirpEnabled = $true
    $global:LastChirpMs  = Get-NowMs

    Run-StrobePhase        # 1- ENTIRE SCREEN BLANKS / CHECKERBOARD
    Run-BjhStrobe          # 2- BJH noise + checkerboard fill
    Run-VoxelPhase         # 2.5- VOXEL POINTCLOUD + checkerboard
    Run-JbjHandshake       # 3- HANDSHAKE using voxel sizes + checkerboard

    $global:ChirpEnabled = $false

    Run-StatinoidzLoop     # 4- TITLE + CHECKERBOARD (~10s, key tones)
    Run-XyPipeline         # 5- XY PIPELINE LOOP WITH CHIRPING KEYPRESSES ONLY
}
