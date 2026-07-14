#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 2 || "$1" != "prove" || "$2" != "build" ]]; then
  printf 'cargo-prove-locked only accepts: prove build [options]\n' >&2
  exit 2
fi

has_docker=0
has_locked=0
for argument in "$@"; do
  [[ "$argument" == "--docker" ]] && has_docker=1
  [[ "$argument" == "--locked" ]] && has_locked=1
done

if [[ "$has_docker" -ne 1 ]]; then
  printf 'cargo-prove-locked requires the reproducible --docker build mode\n' >&2
  exit 2
fi

if [[ "$has_locked" -eq 1 ]]; then
  exec cargo "$@"
fi
exec cargo "$@" --locked
