#!/usr/bin/env bash
# הקמת בסיס הנתונים (Linux / macOS). דורש sqlcmd.
# שימוש: SA_PASSWORD='...' ./setup.sh [server]
set -euo pipefail
cd "$(dirname "$0")"
sqlcmd -S "${1:-localhost}" -U sa -P "${SA_PASSWORD:?set SA_PASSWORD}" -C -b -i 00_run_all.sql \
  -v AppPassword="${APP_PASSWORD:-DcApp#2026!}" ReaderPassword="${READER_PASSWORD:-DcReader#2026!}"
