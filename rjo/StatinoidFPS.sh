#!/usr/bin/env bash
# StatinoidFPS.sh
# Bash port of StatinoidFPS.ps1
# Targets: Android Termux, Ubuntu Linux (native), Windows (fallback via Git Bash/MSYS/WSL)

###############################################################################
# GLOBAL STATE + ENV DETECTION
###############################################################################

COLORS=(
  "white" "brightblack" "green" "yellow" "brightgreen" "green"
  "cyan" "brightcyan" "blue" "brightblue" "magenta" "brightmagenta" "red" "brightred"
)

FPS_LAST_MS=0
FPS_VALUE=0
VOXEL_X=1
VOXEL_Y=1
CHIRP_ENABLED=0
LAST_CHIRP_MS=0

BANNER_LINES=(
"███████╗████████╗ █████╗ ████████╗██╗███╗   ██╗ ██████╗ ██╗█████╗ █████████╗"
"██╔════╝╚██╔═██╔╝██╔══██╗╚██╔═██╔╝██║███║   ██║██╔═══██╗██║██╔═██╗╚═════██═╝"
"███████╗ ██║ ██║ ███████║ ██║ ██║ ██║██╔██╗ ██║██║   ██║██║██║  ██    ██═╝"
"╚════██║ ██║ ██║ ██╔══██║ ██║ ██║ ██║██║╚██╗██║██║   ██║██║██║ ██╝  ██═╝"
"███████║██╔╝ ██║ ██║  ██║██╔╝ ██║ ██║██║ ╚████║╚██████╔╝██║█████╝ ████████╗"
"╚══════╝╚═╝  ╚═╝ ╚═╝  ╚═╝╚═╝  ╚═╝ ╚═╝╚═╝  ╚═══╝ ╚═════╝ ╚═╝╚═══╝  ╚═══════╝"
)
ANCHOR_LINE="Statinoidz______________________________________________________/"

OS_NAME="$(uname -s 2>/dev/null || echo unknown)"
IS_WINDOWS=0
case "$OS_NAME" in
  MINGW*|MSYS*|CYGWIN*|Windows_NT) IS_WINDOWS=1 ;;
esac

if command -v tput >/dev/null 2>&1; then
  HAS_TPUT=1
else
  HAS_TPUT=0
fi

if date +%s%3N >/dev/null 2>&1; then
  HAS_DATE_MS=1
else
  HAS_DATE_MS=0
fi

HAS_BELL=1
ORIG_STTY_SETTINGS="$(stty -g 2>/dev/null || echo "")"

###############################################################################
# TERMINAL HELPERS
###############################################################################

get_now_ms() {
  if [ "$HAS_DATE_MS" -eq 1 ]; then
    date +%s%3N
  else
    printf '%s000\n' "$(date +%s)"
  fi
}

update_fps() {
  local now dt
  now="$(get_now_ms)"
  if [ "$FPS_LAST_MS" -eq 0 ]; then
    FPS_VALUE=0
  else
    dt=$(( now - FPS_LAST_MS ))
    if [ "$dt" -le 0 ]; then
      FPS_VALUE=0
    else
      FPS_VALUE=$(( 1000 / dt ))
    fi
  fi
  FPS_LAST_MS="$now"
}

sleep_ms() {
  local ms="$1"
  if command -v perl >/dev/null 2>&1; then
    perl -e "select(undef, undef, undef, $ms/1000)"
  else
    sleep "$(printf '%0.3f' "$(echo "$ms / 1000" | bc -l 2>/dev/null || echo 0.1)")"
  fi
}

get_window_size() {
  local cols lines
  if [ "$HAS_TPUT" -eq 1 ]; then
    cols="$(tput cols 2>/dev/null || echo 80)"
    lines="$(tput lines 2>/dev/null || echo 24)"
  else
    cols=80
    lines=24
  fi
  WIN_COLS="$cols"
  WIN_LINES="$lines"
}

cursor_to() {
  local x="$1" y="$2"
  if [ "$HAS_TPUT" -eq 1 ]; then
    tput cup "$y" "$x" 2>/dev/null || :
  else
    printf '\033[%d;%dH' "$((y+1))" "$((x+1))"
  fi
}

clear_screen_full() {
  if [ "$HAS_TPUT" -eq 1 ]; then
    tput clear 2>/dev/null || printf '\033[2J\033[H'
  else
    printf '\033[2J\033[H'
  fi
}

set_color() {
  local fg="$1" bg="$2"
  if [ "$HAS_TPUT" -eq 1 ]; then
    local fg_idx bg_idx
    case "$fg" in
      black) fg_idx=0 ;;
      red) fg_idx=1 ;;
      green) fg_idx=2 ;;
      yellow) fg_idx=3 ;;
      blue) fg_idx=4 ;;
      magenta) fg_idx=5 ;;
      cyan) fg_idx=6 ;;
      white) fg_idx=7 ;;
      brightblack) fg_idx=8 ;;
      brightred) fg_idx=9 ;;
      brightgreen) fg_idx=10 ;;
      brightyellow) fg_idx=11 ;;
      brightblue) fg_idx=12 ;;
      brightmagenta) fg_idx=13 ;;
      brightcyan) fg_idx=14 ;;
      brightwhite) fg_idx=15 ;;
      *) fg_idx=7 ;;
    esac
    case "$bg" in
      black) bg_idx=0 ;;
      red) bg_idx=1 ;;
      green) bg_idx=2 ;;
      yellow) bg_idx=3 ;;
      blue) bg_idx=4 ;;
      magenta) bg_idx=5 ;;
      cyan) bg_idx=6 ;;
      white) bg_idx=7 ;;
      brightblack) bg_idx=8 ;;
      brightred) bg_idx=9 ;;
      brightgreen) bg_idx=10 ;;
      brightyellow) bg_idx=11 ;;
      brightblue) bg_idx=12 ;;
      brightmagenta) bg_idx=13 ;;
      brightcyan) bg_idx=14 ;;
      brightwhite) bg_idx=15 ;;
      *) bg_idx=0 ;;
    esac
    tput setaf "$fg_idx" 2>/dev/null || :
    tput setab "$bg_idx" 2>/dev/null || :
  else
    local fg_code bg_code
    case "$fg" in
      black) fg_code=30 ;;
      red) fg_code=31 ;;
      green) fg_code=32 ;;
      yellow) fg_code=33 ;;
      blue) fg_code=34 ;;
      magenta) fg_code=35 ;;
      cyan) fg_code=36 ;;
      white|*) fg_code=37 ;;
    esac
    case "$bg" in
      black|*) bg_code=40 ;;
      red) bg_code=41 ;;
      green) bg_code=42 ;;
      yellow) bg_code=43 ;;
      blue) bg_code=44 ;;
      magenta) bg_code=45 ;;
      cyan) bg_code=46 ;;
      white) bg_code=47 ;;
    esac
    printf '\033[%d;%dm' "$fg_code" "$bg_code"
  fi
}

reset_color() {
  if [ "$HAS_TPUT" -eq 1 ]; then
    tput sgr0 2>/dev/null || printf '\033[0m'
  else
    printf '\033[0m'
  fi
}

maybe_chirp() {
  if [ "$CHIRP_ENABLED" -ne 1 ]; then
    return
  fi
  local now
  now="$(get_now_ms)"
  if [ $(( now - LAST_CHIRP_MS )) -ge 1000 ]; then
    if [ "$HAS_BELL" -eq 1 ]; then
      printf '\a' >/dev/null 2>&1 || :
    fi
    LAST_CHIRP_MS="$now"
  fi
}

###############################################################################
# SOUND SYSTEM (KEY-BASED TONES -> BELL)
###############################################################################

play_tone() {
  local _freq="$1" _dur_ms="$2"
  if [ "$HAS_BELL" -eq 1 ]; then
    printf '\a' >/dev/null 2>&1 || :
  fi
  sleep_ms "$_dur_ms"
}

play_key_chirp() {
  local key="$1"
  case "$key" in
    " ") play_tone 880 60 ;;
    w|W) play_tone 660 60 ;;
    a|A) play_tone 550 60 ;;
    s|S) play_tone 440 60 ;;
    d|D) play_tone 770 60 ;;
    q|Q) play_tone 330 60 ;;
    e|E) play_tone 990 60 ;;
    *)   play_tone 600 40 ;;
  esac
}

###############################################################################
# VOXEL SYSTEM
###############################################################################

update_voxel_sizes() {
  get_window_size
  local cols="$WIN_COLS" lines="$WIN_LINES"
  VOXEL_X=$(( cols / 16 ))
  VOXEL_Y=$(( lines / 16 ))
  [ "$VOXEL_X" -lt 1 ] && VOXEL_X=1
  [ "$VOXEL_Y" -lt 1 ] && VOXEL_Y=1
}

###############################################################################
# CHECKERBOARD HELPERS
###############################################################################

draw_checkerboard_region() {
  local start_row="$1" end_row="$2"
  get_window_size
  local cols="$WIN_COLS" lines="$WIN_LINES"
  [ "$end_row" -gt "$lines" ] && end_row="$lines"

  local y x cx cy
  for (( y=start_row; y<=end_row; y++ )); do
    cursor_to 0 $((y-1))
    local line=""
    for (( x=0; x<cols; x++ )); do
      cx=$(( x / VOXEL_X ))
      cy=$(( y / VOXEL_Y ))
      line="${line} "
    done
    if [ $(( y % 2 )) -eq 0 ]; then
      set_color black white
    else
      set_color black black
    fi
    printf '%s' "$line"
    maybe_chirp
  done
  set_color white black
  reset_color
}

draw_checkerboard_background() {
  local banner_height=$(( ${#BANNER_LINES[@]} + 2 ))
  get_window_size
  draw_checkerboard_region "$banner_height" "$WIN_LINES"
}

###############################################################################
# BANNER + CHECKERBOARD
###############################################################################

show_statinoidz_banner() {
  local color_index="$1"
  update_fps
  update_voxel_sizes
  get_window_size
  local cols="$WIN_COLS"

  clear_screen_full

  cursor_to 0 0
  set_color black white
  local fps_text
  printf -v fps_text 'FPS: %d' "$FPS_VALUE"
  printf '%-*s' "$cols" "$fps_text"

  cursor_to 0 1
  set_color "${COLORS[$(( color_index % ${#COLORS[@]} ))]}" black
  local line
  for line in "${BANNER_LINES[@]}"; do
    printf '%-*s' "$cols" "$line"
    printf '\n'
  done

  set_color brightblack black
  printf '%-*s' "$cols" "$ANCHOR_LINE"

  draw_checkerboard_background

  set_color white black
  reset_color
}

###############################################################################
# VISUAL PHASES
###############################################################################

run_strobe_phase() {
  clear_screen_full
  update_voxel_sizes
  get_window_size
  local lines="$WIN_LINES" cols="$WIN_COLS"

  local i r
  for (( i=0; i<20; i++ )); do
    set_color black white
    cursor_to 0 0
    for (( r=0; r<lines; r++ )); do
      printf '%-*s' "$cols" ""
      printf '\n'
    done
    maybe_chirp
    sleep_ms 60

    clear_screen_full
    draw_checkerboard_region 1 "$lines"
    sleep_ms 60
  done

  set_color white black
  reset_color
}

get_rand_noise() {
  local chars='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()_+=-{}[]<>?/.,'
  local len=32
  local out=""
  local i idx
  for (( i=0; i<len; i++ )); do
    idx=$(( RANDOM % ${#chars} ))
    out="${out}${chars:idx:1}"
  done
  printf '%s\n' "$out"
}

run_bjh_strobe() {
  clear_screen_full
  update_voxel_sizes
  get_window_size
  local cols="$WIN_COLS" lines="$WIN_LINES"

  set_color white black
  cursor_to 0 0
  printf '%-*s' "$cols" "██████████████████████████████████████████████████████████████"
  cursor_to 0 1
  printf '%-*s' "$cols" "██ B ██"
  cursor_to 0 2
  printf '%-*s' "$cols" "██████████████████████████████████████████████████████████████"

  local used_rows=3
  draw_checkerboard_region $((used_rows+1)) "$lines"

  sleep_ms 200

  local i noise
  for (( i=0; i<12; i++ )); do
    noise="$(get_rand_noise)"
    clear_screen_full
    set_color yellow black
    cursor_to 0 0
    printf '%-*s' "$cols" "██████████████████████████████████████████████████████████████"
    cursor_to 0 1
    printf '%-*s' "$cols" "██ ${noise} ██"
    cursor_to 0 2
    printf '%-*s' "$cols" "██████████████████████████████████████████████████████████████"

    draw_checkerboard_region 4 "$lines"
    maybe_chirp
    sleep_ms 80
  done

  set_color white black
  reset_color
}

run_voxel_phase() {
  clear_screen_full
  update_voxel_sizes
  update_fps
  get_window_size
  local cols="$WIN_COLS" lines="$WIN_LINES"

  cursor_to 0 0
  set_color brightblack black
  local header
  printf -v header 'VOXEL PHASE  vx=%d vy=%d  FPS:%d' "$VOXEL_X" "$VOXEL_Y" "$FPS_VALUE"
  printf '%-*s' "$cols" "$header"

  local gx gy
  gx=$(( cols / VOXEL_X ))
  gy=$(( (lines - 1) / VOXEL_Y ))
  [ "$gx" -lt 8 ] && gx=8
  [ "$gy" -lt 4 ] && gy=4

  local row=2 vy vx
  set_color white black
  for (( vy=0; vy<gy; vy++ )); do
    if [ "$row" -ge "$lines" ]; then
      break
    fi
    cursor_to 0 $((row-1))
    local line=""
    for (( vx=0; vx<gx; vx++ )); do
      if [ $(((vx + vy) % 3)) -eq 0 ]; then
        line="${line}·"
      else
        line="${line} "
      fi
    done
    printf '%s' "$line"
    row=$((row+1))
    maybe_chirp
  done

  draw_checkerboard_region "$row" "$lines"
  sleep_ms 600

  set_color white black
  reset_color
}

run_jbj_handshake() {
  clear_screen_full
  update_voxel_sizes
  update_fps
  get_window_size
  local cols="$WIN_COLS" lines="$WIN_LINES"

  cursor_to 0 0
  set_color brightblack black
  local fps_line
  printf -v fps_line 'FPS: %d' "$FPS_VALUE"
  printf '%-*s' "$cols" "$fps_line"

  cursor_to 0 1
  set_color cyan black
  printf '%-*s' "$cols" "JBJ HANDSHAKE"

  local cols_grid rows_grid
  cols_grid=$(( cols / VOXEL_X / 2 ))
  rows_grid=$(( (lines - 2) / VOXEL_Y / 2 ))
  [ "$cols_grid" -lt 4 ] && cols_grid=4
  [ "$rows_grid" -lt 4 ] && rows_grid=4

  set_color yellow black
  local r
  for (( r=0; r<rows_grid; r++ )); do
    if [ $((2 + r)) -ge "$lines" ]; then
      break
    fi
    cursor_to 0 $((2 + r))
    printf '%*s' "$cols_grid" "" | tr ' ' '.'
    maybe_chirp
  done

  local used_rows=$((rows_grid + 2))
  draw_checkerboard_region $((used_rows+1)) "$lines"

  set_color white black
  reset_color
}

###############################################################################
# NON-BLOCKING KEY INPUT
###############################################################################

setup_stty() {
  stty -echo -icanon time 0 min 0 2>/dev/null || :
}

restore_stty() {
  if [ -n "$ORIG_STTY_SETTINGS" ]; then
    stty "$ORIG_STTY_SETTINGS" 2>/dev/null || :
  fi
}

read_key_nonblock() {
  local key
  IFS= read -rsn1 key 2>/dev/null || key=""
  printf '%s' "$key"
}

###############################################################################
# MAIN TITLE LOOP
###############################################################################

run_statinoidz_loop() {
  local idx=0
  show_statinoidz_banner "$idx"

  local start_ms now_ms dt
  start_ms="$(get_now_ms)"

  while :; do
    now_ms="$(get_now_ms)"
    dt=$(( now_ms - start_ms ))
    if [ "$dt" -gt 10000 ]; then
      break
    fi

    local key
    key="$(read_key_nonblock)"
    if [ -n "$key" ]; then
      if [ "$key" = $'\e' ]; then
        break
      fi

      play_key_chirp "$key"

      if [ "$key" = " " ]; then
        update_voxel_sizes
        show_statinoidz_banner "$idx"
        continue
      fi

      idx=$(( (idx + 1) % ${#COLORS[@]} ))
      show_statinoidz_banner "$idx"
    fi

    sleep_ms 200
  done
}

###############################################################################
# XY RENDERING (BLOCKY LIKE PS1, NO SCALING)
###############################################################################

digit_glyph() {
  local d="$1"
  case "$d" in
    0)
      printf '%s\n' "███" "█ █" "█ █" "█ █" "█ █" "█ █" "███"
      ;;
    1)
      printf '%s\n' " ██" "███" " ██" " ██" " ██" " ██" "████"
      ;;
    2)
      printf '%s\n' "███" "  █" "  █" "███" "█  " "█  " "███"
      ;;
    3)
      printf '%s\n' "███" "  █" "  █" "███" "  █" "  █" "███"
      ;;
    4)
      printf '%s\n' "█ █" "█ █" "█ █" "███" "  █" "  █" "  █"
      ;;
    5)
      printf '%s\n' "███" "█  " "█  " "███" "  █" "  █" "███"
      ;;
    6)
      printf '%s\n' "███" "█  " "█  " "███" "█ █" "█ █" "███"
      ;;
    7)
      printf '%s\n' "███" "  █" "  █" "  █" "  █" "  █" "  █"
      ;;
    8)
      printf '%s\n' "███" "█ █" "█ █" "███" "█ █" "█ █" "███"
      ;;
    9)
      printf '%s\n' "███" "█ █" "█ █" "███" "  █" "  █" "███"
      ;;
  esac
}

strike_digit() {
  local i=0 line
  while IFS= read -r line; do
    if [ "$i" -eq 3 ]; then
      printf '%s\n' "███"
    else
      printf '%s\n' "$line"
    fi
    i=$((i+1))
  done
}

render_xy_digits() {
  local X="$1" Y="$2"
  local ax ay ax_str ay_str d1 d2 d3 d4

  ax=${X#-}
  ay=${Y#-}
  printf -v ax_str '%02d' "$ax"
  printf -v ay_str '%02d' "$ay"

  d1="${ax_str:0:1}"
  d2="${ax_str:1:1}"
  d3="${ay_str:0:1}"
  d4="${ay_str:1:1}"

  local g1 g2 g3 g4

  mapfile -t g1 < <(digit_glyph "$d1")
  mapfile -t g2 < <(digit_glyph "$d2")
  mapfile -t g3 < <(digit_glyph "$d3")
  mapfile -t g4 < <(digit_glyph "$d4")

  if [ "$X" -lt 0 ]; then
    mapfile -t g1 < <(printf '%s\n' "${g1[@]}" | strike_digit)
    mapfile -t g2 < <(printf '%s\n' "${g2[@]}" | strike_digit)
  fi
  if [ "$Y" -lt 0 ]; then
    mapfile -t g3 < <(printf '%s\n' "${g3[@]}" | strike_digit)
    mapfile -t g4 < <(printf '%s\n' "${g4[@]}" | strike_digit)
  fi

  local rows=()
  local i
  for (( i=0; i<7; i++ )); do
    rows+=( "${g1[$i]} ${g2[$i]}  ${g3[$i]} ${g4[$i]}" )
  done

  printf '%s\n' "${rows[@]}"
}

get_xy_from_blob() {
  local candidates=()
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" 2>/dev/null && pwd)"
  candidates+=( "$script_dir/StatiBlob.txt" )
  candidates+=( "$(pwd)/StatiBlob.txt" )
  [ -n "$HOME" ] && candidates+=( "$HOME/StatiBlob.txt" )

  local path content x y
  for path in "${candidates[@]}"; do
    if [ -f "$path" ]; then
      content="$(head -n1 "$path" 2>/dev/null || echo "")"
      [ -z "$content" ] && continue
      IFS=' ,;  ' read -r x y _ <<<"$content"
      if [ -n "$x" ] && [ -n "$y" ]; then
        printf '%s %s\n' "$x" "$y"
        return 0
      fi
    fi
  done
  return 1
}

render_xy_fullscreen() {
  local X="$1" Y="$2"
  get_window_size
  local cols="$WIN_COLS" lines="$WIN_LINES"

  update_fps
  clear_screen_full

  cursor_to 0 0
  set_color black white
  local status
  printf -v status 'FPS:%3d  X:%4d  Y:%4d' "$FPS_VALUE" "$X" "$Y"
  printf '%-*s' "$cols" "$status"

  mapfile -t glyph < <(render_xy_digits "$X" "$Y")

  local glyph_rows=${#glyph[@]}
  local top_margin=2
  local usable_lines=$(( lines - top_margin ))
  [ "$usable_lines" -lt "$glyph_rows" ] && usable_lines="$glyph_rows"
  local pad_top=$(( (usable_lines - glyph_rows) / 2 ))
  local current_row=$(( top_margin + pad_top ))

  set_color white black

  local row_text len pad line_out
  for row_text in "${glyph[@]}"; do
    if [ "$current_row" -ge "$lines" ]; then
      break
    fi
    len=${#row_text}
    pad=$(( (cols - len) / 2 ))
    [ "$pad" -lt 0 ] && pad=0
    cursor_to 0 "$current_row"
    printf '%*s%s' "$pad" "" "$row_text"
    current_row=$((current_row+1))
  done

  draw_checkerboard_region $((current_row+1)) "$lines"

  set_color white black
  reset_color
}

run_xy_pipeline() {
  while :; do
    local X Y xy
    if xy="$(get_xy_from_blob)"; then
      X="${xy%% *}"
      Y="${xy##* }"
    else
      X=$(( RANDOM % 199 - 99 ))
      Y=$(( RANDOM % 199 - 99 ))
    fi

    render_xy_fullscreen "$X" "$Y"

    local key
    key="$(read_key_nonblock)"
    if [ -n "$key" ]; then
      if [ "$key" = $'\e' ]; then
        break
      fi
      play_key_chirp "$key"
    fi

    sleep_ms 80
  done
}

###############################################################################
# MAIN LOOP
###############################################################################

main() {
  trap restore_stty EXIT
  setup_stty

  while :; do
    CHIRP_ENABLED=1
    LAST_CHIRP_MS="$(get_now_ms)"

    run_strobe_phase
    run_bjh_strobe
    run_voxel_phase
    run_jbj_handshake

    CHIRP_ENABLED=0

    run_statinoidz_loop
    run_xy_pipeline
  done
}

main "$@"
