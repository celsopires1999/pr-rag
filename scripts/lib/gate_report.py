"""Aggregate N live-gate observations into rates and a verdict.

A live gate measures a stochastic system: the same prompt can produce a correct
answer or a poor one, and one sample cannot tell a regression from a bad roll. So
the gate reports rates over N runs and separates two things a single pass/fail
line cannot:

  HARD  an invariant. Never wrong, never written-unsafely, never lied about. One
        occurrence fails the run, because "the system wrote a requisition nobody
        confirmed" is not a metric to average.
  RATE  a capability. Whether a turn did its job. Model-dependent, so held to a
        rate rather than a count.
  INFRA the turn was not evaluated: a non-2xx, an unparseable body, a report that
        never landed. A provider outage is not a gate verdict and must not be
        debugged as one — but it must not read as a pass either, because a gate
        that reports a dead system as clean is worse than no gate.

Each check declares its own severity where it detects the problem, because that is
where the context lives. This module only aggregates. That is the whole reason
severity is not a lookup table keyed on the problem's text: a code renamed in one
script would otherwise silently become unclassified here.

Usage: one JSON observation per line on stdin, and optional per-check floors:

    {"run": 1, "probe": "stage", "problems": [["HARD", "wrote-before-confirmation"], []]}

    echo "$obs" | gate_report.py --label CREATION \\
        --min-rate '{"no-draft-staged": 0.8}'

An unlisted check defaults to 1.0, so the gate is exactly as strict as a
single-run gate until a relaxation is written down deliberately, with the
measurement that justified it recorded next to the flag.
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import OrderedDict

HARD = "HARD"
RATE = "RATE"
INFRA = "INFRA"

_SEVERITY_ORDER = {HARD: 0, RATE: 1, INFRA: 2}


class Check:
    """One check's tally for one probe across every run."""

    def __init__(self, probe, code, severity, minimum, total):
        self.probe = probe
        self.code = code
        self.severity = severity
        self.minimum = minimum
        self.total = total
        self.failed = 0

    @property
    def rate(self):
        return (self.total - self.failed) / self.total if self.total else 0.0

    @property
    def breached(self):
        return self.severity == RATE and self.rate < self.minimum


def parse_observations(stream):
    observations = []
    for line in stream:
        line = line.strip()
        if not line:
            continue
        try:
            observations.append(json.loads(line))
        except ValueError:
            observations.append(
                {"run": "?", "probe": "?", "problems": [[INFRA, "unparseable-observation"]]}
            )
    return observations


def build_checks(observations, minima):
    """Tally the non-HARD problems; HARD is counted separately, by occurrence."""
    totals = OrderedDict()
    for observation in observations:
        probe = observation.get("probe", "?")
        totals[probe] = totals.get(probe, 0) + 1

    failures = OrderedDict()
    for observation in observations:
        probe = observation.get("probe", "?")
        for problem in observation.get("problems") or []:
            if not problem:
                continue
            severity, code = problem[0], (problem[1] if len(problem) > 1 else "?")
            if severity == INFRA:
                continue
            key = (probe, code)
            entry = failures.get(key)
            if entry is None:
                entry = {
                    "severity": severity,
                    "failed": 0,
                }
                failures[key] = entry
            entry["failed"] += 1

    checks = []
    for (probe, code), entry in failures.items():
        checks.append(
            Check(
                probe=probe,
                code=code,
                severity=entry["severity"],
                minimum=minima.get(code, 1.0),
                total=totals[probe],
            )
        )
        checks[-1].failed = entry["failed"]
    return checks


def count_severity(observations, severity):
    return sum(
        1
        for observation in observations
        for problem in (observation.get("problems") or [])
        if problem and problem[0] == severity
    )


def main(argv=None):
    parser = argparse.ArgumentParser(description="Summarise live-gate observations.")
    parser.add_argument("--min-rate", default="{}", help='JSON map of check code to required rate')
    parser.add_argument("--label", default="GATE", help="name for the final verdict line")
    parser.add_argument(
        "--expect-probes",
        default="",
        help="comma-separated probes that must appear; one that never reported "
        "means its checks never ran, which is not a pass",
    )
    args = parser.parse_args(argv)

    try:
        minima = json.loads(args.min_rate)
    except ValueError as exc:
        print("  FAIL  --min-rate is not valid JSON: %s" % exc)
        return 1

    observations = parse_observations(sys.stdin)
    if not observations:
        print("  FAIL  no observations recorded, so nothing was checked")
        return 1

    hard = count_severity(observations, HARD)
    infra = count_severity(observations, INFRA)
    checks = build_checks(observations, minima)
    breached = [c for c in checks if c.breached]

    # A check that passed leaves no entry, so a failures-only table is not by
    # itself evidence that anything was checked. The per-probe turn counts are:
    # a probe with no turns never ran its checks, and a run that raised no problem
    # at all is the honest reading of "everything held" rather than an empty table
    # that could equally mean nothing was examined.
    per_probe = OrderedDict()
    for observation in observations:
        probe = observation.get("probe", "?")
        clean, seen = per_probe.get(probe, (0, 0))
        raised = [p for p in (observation.get("problems") or []) if p]
        per_probe[probe] = (
            clean + (0 if raised else 1),
            seen + 1,
        )

    print("")
    print("  %-14s %-36s %-7s %-6s %s" % ("probe", "check", "ok", "rate", "need"))
    for check in sorted(
        checks, key=lambda c: (_SEVERITY_ORDER.get(c.severity, 9), c.probe, c.code)
    ):
        print(
            " %s%-13s %-36s %-7s %-6.2f %s"
            % (
                "!" if check.breached else " ",
                check.probe,
                check.code,
                "%d/%d" % (check.total - check.failed, check.total),
                check.rate,
                check.minimum if check.severity == RATE else check.severity,
            )
        )
    if not checks:
        print("  (no check failed)")

    print("")
    for probe, (clean, seen) in per_probe.items():
        marker = "!" if seen == 0 else " "
        print(" %s%-13s %d/%d turns raised nothing" % (marker, probe, clean, seen))

    failed = False
    for check in breached:
        print(
            "  FAIL  %s: %s at %.2f, below the required %.2f"
            % (check.probe, check.code, check.rate, check.minimum)
        )
        failed = True

    if hard:
        print("  FAIL  %d invariant violation(s); these are not averaged." % hard)
        failed = True

    expected = [p for p in args.expect_probes.split(",") if p]
    for probe in expected:
        if probe not in per_probe:
            print("  FAIL  probe '%s' never reported, so its checks never ran." % probe)
            failed = True

    if infra:
        # Not a gate verdict, but not a pass either. Reporting a dead provider as
        # clean is the failure this pair of gates exists to avoid, so an
        # unevaluated turn fails loudly and says why.
        print(
            "  FAIL  %d turn(s) were not evaluated (transport or missing report)."
            % infra
        )
        print("        A provider outage is not a regression, but it is not a pass.")
        failed = True


    print("%s: %s" % (args.label, "FAILED" if failed else "passed"))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
