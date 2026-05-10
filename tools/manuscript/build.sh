#!/usr/bin/env bash
# Builds Neo Elastic Network manuscript PDFs (English + Simplified Chinese).
#
# Inputs:
#   tools/manuscript/manifest-{en,zh}.txt — chapter order
#   tools/manuscript/metadata-{en,zh}.yaml — pandoc metadata
#   docs/**/*.md — chapter sources
#   docs/figures/architecture/*.svg + docs/zh/figures/architecture/*.svg — figures
#
# Outputs:
#   build/manuscript-en.pdf
#   build/manuscript-zh.pdf
#
# Dependencies:
#   * pandoc — apt: pandoc, OR static binary unpacked into ~/.local/pandoc/bin/
#   * xelatex — apt: texlive-xetex texlive-lang-cjk fonts-noto-cjk
#   * SVG→PDF converter, one of:
#     - rsvg-convert (apt: librsvg2-bin), or
#     - python3 + cairosvg (pip: cairosvg)

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

BUILD="$ROOT/build"
WORK="$BUILD/manuscript-tmp"
FIG_EN="$BUILD/manuscript-figures-en"
FIG_ZH="$BUILD/manuscript-figures-zh"
mkdir -p "$BUILD" "$WORK" "$FIG_EN/architecture" "$FIG_ZH/architecture"

# --- locate pandoc (PATH first, then user-local install) ---
if command -v pandoc >/dev/null 2>&1; then
  PANDOC=pandoc
elif [[ -x "$HOME/.local/pandoc/bin/pandoc" ]]; then
  PANDOC="$HOME/.local/pandoc/bin/pandoc"
else
  echo "ERROR: pandoc not found." >&2
  echo "  Install via apt:        sudo apt install pandoc" >&2
  echo "  Or static binary:       curl -sL https://github.com/jgm/pandoc/releases/latest/download/pandoc-3.5-linux-amd64.tar.gz | tar -xz -C ~/.local/pandoc --strip-components=1" >&2
  exit 1
fi

if ! command -v xelatex >/dev/null 2>&1; then
  echo "ERROR: xelatex not found." >&2
  echo "  Install: sudo apt install texlive-xetex texlive-lang-cjk fonts-noto-cjk" >&2
  exit 1
fi

# --- detect SVG converter ---
SVG_TOOL=""
if command -v rsvg-convert >/dev/null 2>&1; then
  SVG_TOOL=rsvg-convert
elif python3 -c "import cairosvg" >/dev/null 2>&1; then
  SVG_TOOL=cairosvg
else
  echo "ERROR: no SVG→PDF tool found." >&2
  echo "  Install one of:" >&2
  echo "    sudo apt install librsvg2-bin     (provides rsvg-convert)" >&2
  echo "    pip3 install --user cairosvg      (pure-Python fallback)" >&2
  exit 1
fi

# --- pre-convert all SVGs in docs/{,zh/}figures to PDF, mirroring the tree ---
convert_svgs() {
  local src_root="$1"
  local out_root="$2"
  if [[ "$SVG_TOOL" == rsvg-convert ]]; then
    while IFS= read -r svg; do
      rel="${svg#$src_root/}"
      out="$out_root/${rel%.svg}.pdf"
      mkdir -p "$(dirname "$out")"
      rsvg-convert -f pdf -o "$out" "$svg"
    done < <(find "$src_root" -name '*.svg')
  else
    python3 - "$src_root" "$out_root" <<'PY'
import cairosvg, os, sys
src_root = os.path.abspath(sys.argv[1])
out_root = os.path.abspath(sys.argv[2])
for dirpath, _, files in os.walk(src_root):
    for f in files:
        if not f.endswith('.svg'): continue
        src = os.path.join(dirpath, f)
        rel = os.path.relpath(src, src_root)
        out = os.path.join(out_root, rel[:-4] + '.pdf')
        os.makedirs(os.path.dirname(out), exist_ok=True)
        cairosvg.svg2pdf(url=src, write_to=out, output_width=900)
PY
  fi
}

echo "=== Converting SVGs → PDF (using $SVG_TOOL) ==="
# Mirror the source figures/ tree under the build dir so markdown
# references like figures/architecture/foo.pdf resolve via --resource-path.
mkdir -p "$FIG_EN/figures" "$FIG_ZH/figures"
convert_svgs "$ROOT/docs/figures"     "$FIG_EN/figures"
echo "  EN figures: $(find $FIG_EN -name '*.pdf' | wc -l)"
convert_svgs "$ROOT/docs/zh/figures"  "$FIG_ZH/figures"
echo "  ZH figures: $(find $FIG_ZH -name '*.pdf' | wc -l)"

# --- per-language build ---
build_lang() {
  local lang="$1"     # en | zh
  local manifest="$ROOT/tools/manuscript/manifest-$lang.txt"
  local metadata="$ROOT/tools/manuscript/metadata-$lang.yaml"
  local out_md="$WORK/manuscript-$lang.md"
  local out_pdf="$BUILD/manuscript-$lang.pdf"
  local fig_dir="$BUILD/manuscript-figures-$lang"

  echo "=== building manuscript-$lang.pdf ==="

  : > "$out_md"
  while IFS= read -r line; do
    [[ -z "$line" || "$line" =~ ^# ]] && continue

    if [[ "$line" =~ ^!\ (.+)$ ]]; then
      printf '\n\\part{%s}\n\n' "${BASH_REMATCH[1]}" >> "$out_md"
      continue
    fi

    if [[ ! -f "$line" ]]; then
      echo "ERROR: manifest line points at missing file: $line" >&2
      exit 1
    fi

    printf '\n\\newpage\n\n' >> "$out_md"
    cat "$line" >> "$out_md"
    printf '\n\n' >> "$out_md"
  done < "$manifest"

  # Rewrite SVG references → PDF references so xelatex can embed them.
  # Matches both <img src="..."> and ![](...) markdown image syntax.
  sed -i -E \
    -e 's|src="(\.\./)*figures/([^"]+)\.svg"|src="figures/\2.pdf"|g' \
    -e 's|\]\((\.\./)*figures/([^)]+)\.svg\)|](figures/\2.pdf)|g' \
    "$out_md"

  # Convert HTML <p align="center"><img src=...></p> blocks to markdown
  # image syntax (![alt](path)). Pandoc renders these as \includegraphics
  # which xelatex actually embeds; raw <img> is dropped silently.
  python3 - "$out_md" <<'PY'
import re, sys
p = sys.argv[1]
with open(p, 'r') as f: s = f.read()

# Match <p align="center"> ... <img src="X" alt="Y" ...> ... </p>
# (multi-line, dot-all so the inner content can wrap)
pat_center = re.compile(
    r'<p\s+align="center">\s*'
    r'<img\s+src="([^"]+)"(?:\s+alt="([^"]*)")?(?:\s+width="(\d+)")?\s*/?>'
    r'\s*</p>',
    re.DOTALL
)
def repl_center(m):
    src, alt, _w = m.group(1), m.group(2) or '', m.group(3)
    # Use markdown image syntax. Pandoc passes alt as the caption.
    return f'\n![{alt}]({src})\n'
s = pat_center.sub(repl_center, s)

# Also handle stand-alone <img src="..."> without the wrapping <p>.
pat_img = re.compile(
    r'<img\s+src="([^"]+)"(?:\s+alt="([^"]*)")?(?:\s+width="(\d+)")?\s*/?>',
    re.DOTALL
)
def repl_img(m):
    src, alt, _w = m.group(1), m.group(2) or '', m.group(3)
    return f'\n![{alt}]({src})\n'
s = pat_img.sub(repl_img, s)

with open(p, 'w') as f: f.write(s)
PY

  # Strip GitHub Actions build badges (won't render in PDF, look ugly).
  sed -i -E '/!\[build\]\(https?:\/\/[^)]*badge\.svg\)/d' "$out_md"
  sed -i -E 's|\[!\[build\]\([^)]+\)\]\([^)]+\)||g' "$out_md"

  # Substitute emoji status indicators with DejaVu-supported equivalents.
  # The PDF font (DejaVu Sans) lacks Unicode emoji glyphs; these substitutions
  # preserve the semantic meaning (status flag) using BMP characters that
  # DejaVu has glyphs for.
  python3 - "$out_md" <<'PY'
import sys
p = sys.argv[1]
with open(p, 'r') as f: s = f.read()
subs = {
  '✅': '✓',  # ✅ → ✓ (yes / completed)
  '❌': '✗',  # ❌ → ✗ (no / failed)
  '\U0001F7E1': '○',  # 🟡 → ○ (partial / scaffolded)
  '\U0001F7E2': '●',  # 🟢 → ●
  '\U0001F534': '●',  # 🔴 → ● (out-of-repo)
  '⏭': '→',  # ⏭ → →
  '✨': '*',       # ✨ → *
  '\U0001F389': '*',   # 🎉 → *
  '⚠️': '!', # ⚠️ → !
  '⚠': '!',       # ⚠ → !
  '\U0001F680': '>>',  # 🚀 → >>
}
for k, v in subs.items():
  s = s.replace(k, v)
with open(p, 'w') as f: f.write(s)
PY

  echo "  concatenated: $(wc -l < "$out_md") lines"

  "$PANDOC" \
    "$metadata" \
    "$out_md" \
    --from=markdown+pipe_tables+yaml_metadata_block+raw_tex+raw_html \
    --pdf-engine=xelatex \
    --resource-path="$fig_dir:$ROOT:$ROOT/docs:$ROOT/docs/zh" \
    --top-level-division=chapter \
    --toc \
    --toc-depth=2 \
    --number-sections \
    --output="$out_pdf"

  echo "  ✓ wrote $out_pdf  ($(du -h "$out_pdf" | cut -f1))"
}

case "${1:-both}" in
  en)   build_lang en ;;
  zh)   build_lang zh ;;
  both) build_lang en; build_lang zh ;;
  *)    echo "usage: $0 [en|zh|both]" >&2; exit 1 ;;
esac

echo
echo "Done."
echo "  English: $BUILD/manuscript-en.pdf"
echo "  Chinese: $BUILD/manuscript-zh.pdf"
