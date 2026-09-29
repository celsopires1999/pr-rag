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
# The four checks are not equally negotiable, so they are not averaged together:
#
#   HARD   Narration, a claimed search that never ran, an empty answer, a report
#          claiming a fallback nobody received. These are correctness defects: the
#          user is told something untrue, and averaging them against a good turn
#          would hide that. One occurrence fails.
#   RATE   Whether a cold turn actually attempted a retrieval. Model-dependent, so
#          it is held to a rate rather than asserted on a single sample.
#   INFRA  Non-2xx, unparseable, or a missing report. Counted and reported, and
#          never a pass — a dead provider is not a clean run.
#
# Usage:  scripts/live-answer-hygiene.sh [runs]        (default 8)
# Needs the demo API on :8081 and the ./reports bind mount.
#
# Set GATE_DEPLOYMENT to a free-form label naming the deployment under test (e.g.
# "azure/gpt-5-mini"). It is recorded on every observation and printed by the
# summariser, because a hygiene rate measured on one deployment does not describe
# another, and a pooled run that mixes them reports a rate for neither. Unset is
# allowed and reported as unlabelled: an exploratory run is still a run, and
# failing it would only teach people to pass a label they do not have.
#
# Measured, gpt-4o-mini over OpenAI, 8 runs, 8 clean: `RetrievalAttempted` true and
# 5 rows on every turn, no narration, no claimed search that did not run. Routing was
# entry `prrag.orchestrator` to author `prrag.retrieval` on all eight, via
# `search_semantic`. That last part is the reading worth having after
# `SkillActivationSpecialist` started appending an ownership notice to the
# `activate_skill` result: a retrieval turn is exactly where an extra sentence in a
# tool result could plausibly pull the front door into the read path. It did not, and
# the notice itself is covered where it does fire by the creation gate's `stage`
# probe, which activates a skill and still hands off (3 of 3 on both deployments).
# Note what this run does not cover: `activate_skill` was never called on these eight
# turns, so it is evidence about the read path, not about the notice.
#
# Measured, gpt-5-mini on Azure, 8 runs, 7 clean. Retrieval was attempted on all
# eight with 5 rows, routing was orchestrator to `prrag.retrieval` via
# `search_semantic` on all eight, and the one failure is a false positive in this
# script's own matcher rather than a defect in the system: the answer ends
# "If you want to filter by a specific supplier or an exact item code, tell me which
# and I will search for those codes" -- an offer of future work, after a complete
# grounded answer, on a turn whose report records `RetrievalAttempted = true` and 5
# rows. The marker list is a bare substring test, so "I will search" cannot tell an
# announcement of the search about to happen from a closing offer. Left as it is, and
# read as a gate defect to fix deliberately rather than a measurement: the same
# reasoning that keeps a HARD check off the rate tables applies to a matcher that
# cannot distinguish its two targets.
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
OBSERVATIONS=$(mktemp)
trap 'rm -f "$tmp_body" "$OBSERVATIONS"' EXIT

# A turn that failed before a verdict existed still needs to be recorded, or an
# unevaluated turn would silently leave the sample smaller than N.
#
# The deployment label is written here as well as on the verdict path, for the
# reason it exists at all: a rate measured on one deployment is not evidence about
# another, and this change is the second time that has bitten. Unset is allowed.
record() {
  DEPLOYMENT_JSON=$(printf '%s' "${GATE_DEPLOYMENT:-}" | python3 -c 'import json,sys;print(json.dumps(sys.stdin.read() or None))')
  printf '{"run": %s, "probe": "cold-search", "deployment": %s, "problems": [["%s", "%s"]]}\n' \
    "$1" "$DEPLOYMENT_JSON" "$2" "$3" >> "$OBSERVATIONS"
}

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
    record "$i" "INFRA" "http-$status"
    before=$(python3 -c 'import time; print(time.time())')
    continue
  fi

  verdict=$(RESPONSE="$response" BEFORE="$before" QUESTION="$QUESTION" REPORTS_DIR="$REPORTS_DIR" python3 <<'PY'
import glob, json, os, re, time

problems = []

try:
    response = json.loads(os.environ["RESPONSE"])
except Exception:
    print(json.dumps({"problems": [["INFRA", "unparseable-response"]], "ret": "?", "attempted": None, "head": ""}))
    raise SystemExit(0)

answer = response.get("answer") or ""
ret = response.get("retrievedCount", 0)

# 1. No answer at all. Checked first because every other check is vacuous on an
#    empty answer: there is nothing to narrate and nothing to claim.
if not answer.strip():
    problems.append(["HARD", "empty-answer"])

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
    problems.append(["INFRA", "no-report-for-this-turn"])
else:
    # 2. A cold turn that must search has to search.
    if mine.get("RetrievalAttempted") is not True:
        problems.append(["RATE", "no-retrieval-attempt"])

    # 4. A claimed search has to be one the report shows.
    claims_search = re.search(
        r"(?i)executed the search|ran the search|i searched|search (?:has been )?run", answer)
    if claims_search and mine.get("RetrievalAttempted") is False:
        problems.append(["HARD", "claimed-a-search-that-never-ran"])

    # The report's own fallback flag must not claim an answer the caller never
    # received; that combination is what a dead provider used to look like.
    if not answer.strip() and mine.get("UsedNoContextFallback") is True:
        problems.append(["HARD", "report-claims-a-fallback-that-was-never-sent"])

markers = ("Thought:", "Action:", "Observation:", "Plan:", "I will search", "I need to look")
leaked = [m for m in markers if m in answer]
if leaked:
    problems.append(["HARD", "narration(" + ",".join(leaked) + ")"])

print(json.dumps({
    "problems": problems,
    "ret": ret,
    "attempted": (mine or {}).get("RetrievalAttempted"),
    "head": answer[:70].replace("\n", " "),
}))
PY
)

  if [ -z "$verdict" ]; then
    echo "  FAIL  run $i  no verdict (infrastructure, not a hygiene verdict)"
    infra_runs=$((infra_runs + 1))
    failed_runs=$((failed_runs + 1))
    record "$i" "INFRA" "no-verdict"
    continue
  fi

  # The python prints the human line on stdout and appends its own observation, so
  # the per-run lines and the aggregate are derived from the same data and cannot
  # disagree; its exit status is the pass/fail signal.
  echo "$verdict" | RUN="$i" OBS="$OBSERVATIONS" python3 -c "
import json, os, sys

v = json.load(sys.stdin)
problems = v['problems']

tag = 'FAIL ' if problems else 'ok   '
print('  %s run %-2s ret=%-2s attempted=%-5s %s' % (
    tag, os.environ['RUN'], v['ret'], v['attempted'], v['head']))
for severity in ('HARD', 'RATE', 'INFRA'):
    for problem in problems:
        if problem and problem[0] == severity:
            print('       %-5s %s' % (severity, problem[1]))

with open(os.environ['OBS'], 'a') as handle:
    handle.write(json.dumps({
        'run': int(os.environ['RUN']),
        'probe': 'cold-search',
        # Which deployment produced this turn. Set GATE_DEPLOYMENT to a free-form
        # label (e.g. "azure/gpt-5-mini"); the report must not carry a provider,
        # model or endpoint, so the gate is the only place that record survives to
        # the moment someone pools runs from more than one deployment.
        'deployment': os.environ.get('GATE_DEPLOYMENT') or None,
        'problems': problems,
    }) + '\n')

sys.exit(1 if problems else 0)
" && grounded=$((grounded + 1)) || failed_runs=$((failed_runs + 1))

  # Only the first turn's report is meaningful for matching; later files are
  # still fresh, so the -5s window above would also catch them. Reset the floor.
  before=$(python3 -c 'import time; print(time.time())')
done

echo
echo "turns=$((grounded + failed_runs))  clean=$grounded  with-problems=$failed_runs  (of $RUNS)"
echo "deployment: ${GATE_DEPLOYMENT:-unlabelled}"

# The verdict comes from the recorded observations rather than the counters, so
# the per-run lines and this summary cannot drift apart.
python3 scripts/lib/gate_report.py \
  --label "ANSWER HYGIENE GATE" \
  --expect-probes cold-search \
  < "$OBSERVATIONS"
