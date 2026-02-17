#!/usr/bin/env bash
set -euo pipefail
if ! command -v brew >/dev/null 2>&1; then
  echo "Homebrew not found. Install from https://brew.sh first." >&2
  exit 1
fi
brew update
brew install qemu
echo "Done. qemu-img should now be at \$(brew --prefix)/bin/qemu-img (on PATH)."
echo "You can also copy that binary next to ConverterApp in the macOS publish folder."
