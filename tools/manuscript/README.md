# Manuscript builder

Builds Neo Elastic Network manuscript PDFs from the existing
`docs/` and `docs/zh/` markdown trees.

## Outputs

```
build/manuscript-en.pdf   # English manuscript
build/manuscript-zh.pdf   # Simplified Chinese manuscript
```

Both PDFs share the same chapter ordering: front matter → architecture
→ operator guide → external bridge → implementation plans. Source
files are concatenated per the manifest, headings are demoted by 1
so each file's H1 becomes a chapter inside its part, and Pandoc emits
a table of contents + numbered sections.

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

# 2. Build both languages (default)
./tools/manuscript/build.sh

# Or build one at a time
./tools/manuscript/build.sh en
./tools/manuscript/build.sh zh
```

The build runs out of `build/manuscript-tmp/` (concatenated markdown,
intermediate `.tex`); the final PDFs land in `build/`.

## How it works

1. **Manifest** (`manifest-en.txt`, `manifest-zh.txt`) defines chapter
   order. Lines starting with `!` introduce a `\part{}` divider; lines
   starting with `#` are comments; everything else is a path relative
   to the repo root.
2. **Metadata** (`metadata-en.yaml`, `metadata-zh.yaml`) carries
   Pandoc YAML metadata: title, subtitle, author, date, document
   class, fonts (CJK fonts for the zh build), TOC depth, page-style
   header/footer, abstract.
3. **Build script** (`build.sh`) walks the manifest, concatenates the
   markdown files (with `\part{}` dividers and `\newpage` between
   chapters), then runs Pandoc with `--pdf-engine=xelatex` to produce
   the PDF.

SVG figures embedded as `<img src="figures/architecture/X.svg">` are
resolved by Pandoc via `--resource-path` and rendered into the PDF
through `rsvg-convert` (which Pandoc invokes automatically when it
sees an `.svg` extension).

## Customizing chapter order

Edit `manifest-en.txt` / `manifest-zh.txt`. The build script picks
up changes on the next run; no other configuration needed.

## Customizing styling

Edit `metadata-en.yaml` / `metadata-zh.yaml`. Common tweaks:

| Field             | Purpose                                          |
|-------------------|--------------------------------------------------|
| `documentclass`   | `book` (default), `report`, `article`            |
| `fontsize`        | `10pt`, `11pt` (default), `12pt`                 |
| `linestretch`     | Body line spacing — 1.15 (en), 1.4 (zh)         |
| `mainfont`        | Latin body font                                  |
| `CJKmainfont`     | CJK body font (zh only) — Noto Serif CJK SC     |
| `geometry`        | Margins, paper size                              |
| `toc-depth`       | TOC nesting depth (default 2)                    |
| `colorlinks`      | Toggles colored hyperlinks                       |

## Troubleshooting

**`xelatex: undefined control sequence \part`** — the build script
uses `\part{}` for part dividers, which requires `documentclass: book`
or `report`. Don't change to `article`.

**Chinese PDF has missing glyphs** — install
`fonts-noto-cjk-extra` for fuller coverage, or change `CJKmainfont`
in `metadata-zh.yaml` to a different installed CJK font (run
`fc-list :lang=zh-cn` to enumerate).

**SVG figures missing from the PDF** — `librsvg2-bin` provides
`rsvg-convert`, which Pandoc invokes for SVG embedding. Verify with
`which rsvg-convert`. If you skip it, Pandoc falls back to inserting
a placeholder.

**Hyperlinks broken in the PDF** — Pandoc's GFM reader handles
markdown links automatically. If a specific link breaks, check the
source markdown — a relative `./X.md` link inside a chapter resolves
to a section in the same PDF only if the target file is also in the
manuscript.
