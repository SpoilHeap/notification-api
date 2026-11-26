#!/usr/bin/env bash
# Coverage gate (engineering_standards.md §4.6).
# Reads ReportGenerator's Summary.json and enforces:
#   - line coverage >= 80% overall
#   - line coverage >= 90% for NotificationApi.Domain and NotificationApi.Application
# Assemblies with no coverable lines are skipped.
# Usage: scripts/check-coverage.sh [path/to/Summary.json]
set -euo pipefail

SUMMARY="${1:-coverage/Summary.json}"
OVERALL_MIN="${COVERAGE_OVERALL_MIN:-80}"
CORE_MIN="${COVERAGE_CORE_MIN:-90}"
CORE_ASSEMBLIES=("NotificationApi.Domain" "NotificationApi.Application")

[[ -f "$SUMMARY" ]] || { echo "✗ $SUMMARY not found (run ReportGenerator with -reporttypes:JsonSummary)" >&2; exit 1; }

failed=0
below() { awk -v a="$1" -v b="$2" 'BEGIN { exit !(a < b) }'; }

overall="$(jq -r '.summary.linecoverage // 0' "$SUMMARY")"
if below "$overall" "$OVERALL_MIN"; then
  echo "✗ overall line coverage ${overall}% < ${OVERALL_MIN}%"
  failed=1
else
  echo "✓ overall line coverage ${overall}% (min ${OVERALL_MIN}%)"
fi

for name in "${CORE_ASSEMBLIES[@]}"; do
  read -r coverable coverage < <(jq -r --arg n "$name" \
    '.coverage.assemblies[] | select(.name == $n) | "\(.coverablelines) \(.coverage // 0)"' "$SUMMARY") || true
  if [[ -z "${coverable:-}" ]]; then
    echo "✗ $name not found in $SUMMARY"
    failed=1
    continue
  fi
  if [[ "$coverable" == "0" ]]; then
    echo "- $name: no coverable lines, skipped"
    unset coverable coverage
    continue
  fi
  if below "$coverage" "$CORE_MIN"; then
    echo "✗ $name line coverage ${coverage}% < ${CORE_MIN}%"
    failed=1
  else
    echo "✓ $name line coverage ${coverage}% (min ${CORE_MIN}%)"
  fi
  unset coverable coverage
done

exit "$failed"
