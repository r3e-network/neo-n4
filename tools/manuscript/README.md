# Manuscript builder

Builds Neo Elastic Network manuscript PDFs from the `docs/` and `docs/zh/`
markdown trees.

## Outputs

```
build/manuscript-en.pdf              # English, full     (~196 pp)
build/manuscript-zh.pdf              # Chinese, full     (~196 pp)
build/manuscript-en-essentials.pdf   # English, design-only (~71 pp)
build/manuscript-zh-essentials.pdf   # Chinese, design-only (~70 pp)
```

The **full** edition collects every doc — architecture chapters,
operator how-tos, external-bridge protocol, implementation plans.

The **essentials** edition is a focused, design-only subset for
newcomers: architecture, modules, and how the pieces fit together.
No tutorials, no byte-layout / wire-format detail. 5 chapters
(`ARCHITECTURE.md`, `architecture-walkthrough.md`,
`architecture-l1-vs-l2.md`, `architecture-l2-lifecycle.md`,
`architecture-trust-boundaries.md`).

## Usage

```bash
# 1. Install dependencies (Debian/Ubuntu — once)
sudo apt install \
    pandoc \
    librsvg2-bin \
    texlive-xetex \
    texlive-fonts-recommended \
    texlive-latex-extra \
    texlive-lang-cjk \
    fonts-noto-cjk

# Or, no-root: install pandoc + cairosvg via pip
#   curl -sL https://github.com/jgm/pandoc/releases/.../pandoc-3.5-linux-amd64.tar.gz \
#     | tar -xz -C ~/.local/pandoc --strip-components=1
#   pip install --user cairosvg

# 2. Build everything (default = full only, both languages)
./tools/manuscript/build.sh

# Build verbs
./tools/manuscript/build.sh en               # full English only
./tools/manuscript/build.sh zh               # full Chinese only
./tools/manuscript/build.sh both             # full both languages (= default)
./tools/manuscript/build.sh en-essentials    # essentials English
./tools/manuscript/build.sh zh-essentials    # essentials Chinese
./tools/manuscript/build.sh essentials       # essentials both languages
./tools/manuscript/build.sh all              # full + essentials, both langs (4 PDFs)
```

The build runs out of `build/manuscript-tmp/` (concatenated markdown,
intermediate `.tex`); final PDFs land in `build/`.

## How it works

1. **Manifest** files (`manifest-{en,zh}.txt`,
   `manifest-{en,zh}-essentials.txt`) define per-variant chapter order.
   Lines starting with `!` introduce a `\part{}` divider; lines
   starting with `#` are comments; everything else is a path relative
   to the repo root.
2. **Metadata** files (`metadata-{en,zh}.yaml`,
   `metadata-{en,zh}-essentials.yaml`) carry Pandoc YAML metadata:
   title, subtitle, author, date, document class, fonts, TOC depth,
   page-style header/footer, abstract.
3. **Build script** (`build.sh`) walks the manifest, concatenates the
   markdown files (with `\part{}` dividers and `\newpage` between
   chapters), strips GitHub badges, normalizes emoji glyphs, rewrites
   raw `<img>` tags to markdown image syntax, then runs Pandoc with
   `--pdf-engine=xelatex` to produce the PDF.

SVG figures (`docs/figures/**.svg` + `docs/zh/figures/**.svg`) are
pre-converted to PDF via either:

  - `rsvg-convert` (apt: `librsvg2-bin`) — preferred, faster, OR
  - `python3` + `cairosvg` (`pip install --user cairosvg`) — fallback

The build script auto-detects whichever is available.

## Adding or reordering chapters

Edit the relevant `manifest-{en,zh}{,-essentials}.txt`. The build
script picks up changes on the next run; no other configuration
needed.

## Customizing styling

Edit the relevant `metadata-{en,zh}{,-essentials}.yaml`. Common tweaks:

| Field             | Purpose                                          |
|-------------------|--------------------------------------------------|
| `documentclass`   | `report` (default), `book`, `article`            |
| `fontsize`        | `10pt`, `11pt` (default), `12pt`                 |
| `linestretch`     | Body line spacing — 1.15 (en), 1.4 (zh)          |
| `mainfont`        | Latin body font                                  |
| `CJKmainfont`     | CJK body font (zh only) — Noto Serif CJK SC      |
| `geometry`        | Margins, paper size                              |
| `toc-depth`       | TOC nesting depth (default 2)                    |
| `colorlinks`      | Toggles colored hyperlinks                       |

## Troubleshooting

**`xelatex: undefined control sequence \part`** — the build uses
`\part{}` for part dividers; requires `documentclass: report` or
`book`. Don't change to `article`.

**Chinese figures render as boxes (□□□)** — the SVGs need a CJK
font listed FIRST in `font-family` (cairo locks in the first
resolvable family per text element; no per-glyph fallback once
chosen). All ZH SVGs in `docs/zh/figures/` are already configured
with `'Noto Sans CJK SC'` first; if you add new ones, mirror that.
Inner `<style>` classes that override font-family (`.field`,
`.size`, `.cli`, `.wire`, `.ctr-name`, etc.) need
`'Noto Sans Mono CJK SC'` first too. Verify with `pdffonts <pdf>` —
look for `NotoSansCJK*`. See `memory/project_zh_svg_cjk_font.md`
for full context.

**Chinese PDF has missing glyphs in body text** — install
`fonts-noto-cjk-extra` for fuller coverage, or change `CJKmainfont`
in `metadata-zh.yaml` to a different installed CJK font (run
`fc-list :lang=zh-cn` to enumerate).

**SVG figures missing from the PDF** — install one of
`librsvg2-bin` or `cairosvg`. The build script will tell you which
it found at the top of the run (`=== Converting SVGs → PDF (using
$tool) ===`).

**Hyperlinks broken in the PDF** — the build uses Pandoc's
`+gfm_auto_identifiers` extension so internal anchor links match
GitHub-style slugs (preserves leading numbers in headings like
`## 8. Foo` → `#8-foo`). If a link still breaks, check the source
markdown — a relative `./X.md` link inside a chapter resolves to
a section in the same PDF only if the target file is also in the
manuscript.

## Reproducing the figure-cache invalidation

If figures look stale (e.g. you edited an SVG and the PDF still
shows the old version), the per-language `build/manuscript-figures-{en,zh}/`
cache may need to be cleared:

```bash
rm -rf build/manuscript-figures-en build/manuscript-figures-zh
./tools/manuscript/build.sh all
```

The cache is keyed on filename, not content hash — so SVG edits
without a filename change can be served from cache.
