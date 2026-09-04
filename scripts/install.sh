#!/usr/bin/env bash
# Run ONCE after cloning, before your first commit:
#   bash scripts/install.sh
#
# Git has no mechanism to auto-activate hooks on clone (this is a git
# limitation, not a gap in this tool) -- this script is the one manual
# step. CI (.github/workflows/enforce.yml) is the backstop that catches
# anyone who skips it or commits with --no-verify.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

python3 -m pip install -q -r requirements.txt --break-system-packages 2>/dev/null \
  || python3 -m pip install -q -r requirements.txt

chmod +x .githooks/pre-commit .githooks/pre-push
git config core.hooksPath .githooks

echo "installed: git hooks active (core.hooksPath=.githooks)"
python3 -m gateway.validate_repo
