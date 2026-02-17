#!/usr/bin/env bash
set -euo pipefail
echo "Installing qemu-img via your package manager..."
if command -v apt >/dev/null 2>&1; then
  sudo apt-get update
  sudo apt-get install -y qemu-utils
elif command -v dnf >/dev/null 2>&1; then
  sudo dnf install -y qemu-img
elif command -v pacman >/dev/null 2>&1; then
  sudo pacman -Syu --noconfirm qemu-img
else
  echo "Please install qemu-img with your distro's package manager." >&2
  exit 1
fi
echo "Done. Ensure qemu-img is on PATH or copy it next to ConverterApp."
