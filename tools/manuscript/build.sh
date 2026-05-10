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
# Dependencies (debian/ubuntu):
#   sudo apt install pandoc librsvg2-bin texlive-xetex texlive-fonts-recommended \
#                    texlive-latex-extra texlive-lang-cjk fonts-noto-cjk
#   (the texlive-* packages are likely already installed if xelatex is present)

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"

BUILD="$ROOT/build"
WORK="$BUILD/manuscript-tmp"
mkdir -p "$BUILD" "$WORK"

# --- preflight ---
for tool in pandoc xelatex rsvg-convert; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "ERROR: $tool not found on PATH." >&2
    case $tool in
      pandoc)         echo "  install: sudo apt install pandoc" >&2 ;;
      rsvg-convert)   echo "  install: sudo apt install librsvg2-bin" >&2 ;;
      xelatex)        echo "  install: sudo apt install texlive-xetex texlive-lang-cjk" >&2 ;;
    esac
    exit 1
  fi
done

# --- per-language build ---
build_lang() {
  local lang="$1"     # en | zh
  local manifest="$ROOT/tools/manuscript/manifest-$lang.txt"
  local metadata="$ROOT/tools/manuscript/metadata-$lang.yaml"
  local out_md="$WORK/manuscript-$lang.md"
  local out_pdf="$BUILD/manuscript-$lang.pdf"

  echo "=== building manuscript-$lang.pdf ==="

  # Concatenate files per the manifest, demoting heading levels by 1
  # (each file's H1 becomes a chapter; in pandoc book class, H1 = part if
  # we let it through, but we want the manifest's "!" lines to be parts).
  : > "$out_md"
  while IFS= read -r line; do
    # Skip comments + blanks
    [[ -z "$line" || "$line" =~ ^# ]] && continue

    if [[ "$line" =~ ^!\ (.+)$ ]]; then
      # Part divider — pandoc emits these as actual \part{} in book class.
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

  echo "  concatenated: $(wc -l < "$out_md") lines"

  # Run pandoc → xelatex.
  # --resource-path lets pandoc find figures relative to the repo root.
  # --shift-heading-level-by=1 demotes everything by 1 so each file's
  # H1 ("Getting started", etc.) becomes a chapter inside its part.
  pandoc \
    "$metadata" \
    "$out_md" \
    --from=gfm+pipe_tables+yaml_metadata_block+raw_tex \
    --pdf-engine=xelatex \
    --shift-heading-level-by=1 \
    --resource-path="$ROOT:$ROOT/docs:$ROOT/docs/zh" \
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
