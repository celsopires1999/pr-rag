#!/usr/bin/env bash
#
# Live answer-hygiene gate.
#
# Why this is a script and not a test: the failure is model behaviour, and a fake
# model does not narrate, so no unit test can see it. AnswerHygienePromptTests
# asserts the cause (the prompt); only a live run can assert the effect. This is
# that run, made repeatable instead of ad-hoc.
#
# Four things are checked per turn:
#
#   1. The turn produced an answer at all. An empty answer used to pass: the first
#      version of this gate only looked for narration and fabricated claims, so a
#      completely dead provider — every turn 200 OK, answer "", no tool calls —
#      was reported as "clean" for all eight runs. A gate that cannot tell a
#      working system from a total outage is worse than no gate, because it is
#      trusted.
#   2. The turn actually attempted a retrieval. Each run uses a fresh session, so
#      a cold turn that must search has to search.
#   3. No reasoning scaffolding in the user-visible answer. The core prompt used
#      to mandate a literal "Thought:/Action:/Observation:" trace and then forbid
#      showing it; the model sometimes printed it.
#   4. No claimed retrieval that did not happen. If the answer says a search was
#      executed, the turn's report must have RetrievalAttempted = true. This is
#      the fabricated-completion shape: the model narrated all three labels,
#      wrote "Observation: Executed the search", and called no tool at all.
#
# A turn with no report is a failure, not an unverifiable pass: checks 2 and 4 read
# the report, so a missing report means the turn was never actually checked.
#
# Usage:  scripts/live-answer-hygiene.sh [runs]        (default 8)
# Needs the demo API on :8081 and the ./reports bind mount.
set -uo pipefail

cd "$(dirname "$0")/.."

RUNS="${1:-8}"
BASE_URL="${BASE_URL:-http://localhost:8081}"
REPORTS_DIR="${REPORTS_DIR:-./reports}"

# The semantic path on purpose: it asks the model to rewrite the query before
# searching, which is where the leak surfaced most often.
QUESTION="show me requisitions for hydraulic equipment"

if ! curl -sf -m 10 "$BASE_URL/api/status" >/dev/null 2>&1; then
  echo "API not reachable at $BASE_URL. Start it with:"
  echo "  API_PORT=8081 docker compose --profile demo up -d --build api"
  exit 2
fi

before=$(python3 -c 'import time; print(time.time())')

narrated=0
fabricated=0
grounded=0
failed_runs=0
infra_runs=0
tmp_body=$(mktemp)
trap 'rm -f "$tmp_body"' EXIT

for i in $(seq 1 "$RUNS"); do
  session="hygiene-$(openssl rand -hex 6)"
  body=$(python3 -c 'import json,sys;print(json.dumps({"session_id":sys.argv[1],"question":sys.argv[2],"top_k":5,"min_similarity":0.3}))' "$session" "$QUESTION")

  status=$(curl -s -m 180 -o "$tmp_body" -w '%{http_code}' \
    -X POST "$BASE_URL/api/chat" -H 'Content-Type: application/json' -d "$body")
  response=$(cat "$tmp_body")

  # A non-2xx is reported as an infrastructure failure, kept apart from the
  # hygiene verdict: a 502 means the provider failed, which says nothing about
  # whether the prompt is leaking.
  if [ "$status" != "200" ]; then
    echo "  FAIL  run $i  http=$status  request failed (infrastructure, not a hygiene verdict)"
    infra_runs=$((infra_runs + 1))
    failed_runs=$((failed_runs + 1))
    before=$(python3 -c 'import time; print(time.time())')
    continue
  fi

  verdict=$(RESPONSE="$response" BEFORE="$before" QUESTION="$QUESTION" REPORTS_DIR="$REPORTS_DIR" python3 <<'PY'
import glob, json, os, re, time

problems = []

try:
    response = json.loads(os.environ["RESPONSE"])
except Exception:
    print(json.dumps({"problems": ["unparseable-response"], "ret": "?", "attempted": None, "head": ""}))
    raise SystemExit(0)

answer = response.get("answer") or ""
ret = response.get("retrievedCount", 0)

# 1. No answer at all. Checked first because every other check is vacuous on an
#    empty answer: there is nothing to narrate and nothing to claim.
if not answer.strip():
    problems.append("empty-answer")

# The report is written before the turn is allowed to succeed, so poll briefly
# rather than assuming it has landed: a missing report would otherwise be
# indistinguishable from a slow write.
reports = []
deadline = time.time() + 5
while True:
    reports = [
        f for f in glob.glob(os.path.join(os.environ["REPORTS_DIR"], "*.json"))
        if os.path.getsize(f) > 0 and os.path.getmtime(f) >= float(os.environ["BEFORE"]) - 5
    ]
    if reports or time.time() > deadline:
        break
    time.sleep(0.25)

mine = None
for f in sorted(reports, key=os.path.getmtime, reverse=True):
    try:
        candidate = json.load(open(f))
    except Exception:
        continue
    if candidate.get("Question") == os.environ["QUESTION"]:
        mine = candidate
        break

if mine is None:
    problems.append("no-report-for-this-turn")
else:
    # 2. A cold turn that must search has to search.
    if mine.get("RetrievalAttempted") is not True:
        problems.append("no-retrieval-attempt")

    # 4. A claimed search has to be one the report shows.
    claims_search = re.search(
        r"(?i)executed the search|ran the search|i searched|search (?:has been )?run", answer)
    if claims_search and mine.get("RetrievalAttempted") is False:
        problems.append("claimed-a-search-that-never-ran")

    # The report's own fallback flag must not claim an answer the caller never
    # received; that combination is what a dead provider used to look like.
    if not answer.strip() and mine.get("UsedNoContextFallback") is True:
        problems.append("report-claims-a-fallback-that-was-never-sent")

markers = ("Thought:", "Action:", "Observation:", "Plan:", "I will search", "I need to look")
leaked = [m for m in markers if m in answer]
if leaked:
    problems.append("narration(" + ",".join(leaked) + ")")

print(json.dumps({
    "problems": problems,
    "ret": ret,
    "attempted": (mine or {}).get("RetrievalAttempted"),
    "head": answer[:70].replace("\n", " "),
}))
PY
)

  echo "$verdict" | python3 -c "
import json, sys
v = json.load(sys.stdin)
tag = 'FAIL ' if v['problems'] else 'ok   '
print('  %s run %-2s ret=%-2s attempted=%-5s %s %s' % (
    tag, '$i', v['ret'], v['attempted'], v['head'],
    ('<- ' + '; '.join(v['problems'])) if v['problems'] else ''))
sys.exit(1 if v['problems'] else 0)
" && grounded=$((grounded + 1)) || failed_runs=$((failed_runs + 1))

  # Only the first turn's report is meaningful for matching; later files are
  # still fresh, so the -5s window above would also catch them. Reset the floor.
  before=$(python3 -c 'import time; print(time.time())')
done

echo
echo "clean=$grounded  failed=$failed_runs  (of $RUNS)"
if [ "$infra_runs" -gt 0 ]; then
  echo "  ($infra_runs run(s) failed at the transport/provider level, not on hygiene)"
fi
if [ "$failed_runs" -gt 0 ]; then
  echo "ANSWER HYGIENE GATE: FAILED"
  exit 1
fi
echo "ANSWER HYGIENE GATE: passed"
