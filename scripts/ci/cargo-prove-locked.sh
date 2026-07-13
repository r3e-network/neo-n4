#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 2 || "$1" != "prove" || "$2" != "build" ]]; then
  printf 'cargo-prove-locked only accepts: prove build [options]\n' >&2
  exit 2
fi

exec cargo "$@" --locked
