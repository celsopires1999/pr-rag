#!/usr/bin/env python3
"""Standalone tests for `gate_report.py`, the live gate's aggregation step.

Run them with:

    python3 -m unittest discover -s scripts/lib -t scripts/lib -v

Why this has a test at all, when the thing it covers only runs against a live
provider: `gate_report.py` is the part that decides whether a run passes, and its
failure modes are all silent. It reports rates, so a check that passed raises no
row and a run where every turn was unevaluated raises no row either — both print
an empty table. The pair of live gates exists because a gate that reports a dead
provider as eight clean runs is worse than no gate, and that failure is in
*this* file's output, not in the shell that collects turns. Only a fixture can
replay a dead provider, because a live run would need one.

So the observations are written here rather than produced by a model. That is
also why nothing in this file asserts on a prompt: the checks under test are the
classification, the floors, and the reporting of an absence.
"""

from __future__ import annotations

import io
import json
import sys
import unittest
from contextlib import redirect_stdout

import gate_report


def observation(run: int, probe: str, problems=None) -> str:
    """One JSONL observation, the shape the live gates emit."""
    return json.dumps({"run": run, "probe": probe, "problems": problems or []})


def run_gate(observations, *args) -> tuple[int, str]:
    """Feeds observations to the aggregator and returns (exit code, output)."""
    stdin, stdout = sys.stdin, io.StringIO()
    sys.stdin = io.StringIO("\n".join(observations) + "\n")
    try:
        with redirect_stdout(stdout):
            code = gate_report.main(list(args))
    finally:
        sys.stdin = stdin

    return code, stdout.getvalue()


class CleanRunTests(unittest.TestCase):
    def test_a_run_with_no_problems_passes(self):
        code, out = run_gate(
            [observation(1, "stage"), observation(1, "confirmed")],
            "--label", "GATE",
        )

        self.assertEqual(code, 0)
        self.assertIn("GATE: passed", out)

    def test_a_clean_run_says_no_check_failed_rather_than_printing_an_empty_table(self):
        _, out = run_gate([observation(1, "stage")], "--label", "GATE")

        # The spec is explicit: an empty table is stated as such, because an empty
        # table and a run that examined nothing look the same otherwise.
        self.assertIn("(no check failed)", out)

    def test_a_clean_run_reports_per_probe_turn_counts(self):
        """A clean run must still show what was checked.

        A check that passes raises no row, so "everything held" and "nothing was
        examined" are byte-identical in a failures-only table. The per-probe lines
        are what make a clean result readable as checked-and-clean.
        """
        code, out = run_gate(
            [
                observation(1, "stage"),
                observation(2, "stage"),
                observation(1, "confirmed"),
            ],
            "--label", "GATE",
        )

        self.assertEqual(code, 0)
        self.assertIn("stage", out)
        self.assertIn("2/2 turns raised nothing", out)
        self.assertIn("confirmed", out)
        self.assertIn("1/1 turns raised nothing", out)

    def test_a_turn_that_raised_a_problem_is_counted_as_having_raised_something(self):
        _, out = run_gate(
            [
                observation(1, "no-confirm", [["RATE", "no-draft-staged"]]),
                observation(2, "no-confirm"),
            ],
            "--min-rate", '{"no-draft-staged": 0.0}',
        )

        self.assertIn("1/2 turns raised nothing", out)


class SeverityClassificationTests(unittest.TestCase):
    def test_an_invariant_violation_fails_on_a_single_occurrence(self):
        code, out = run_gate(
            [
                observation(1, "stage"),
                observation(2, "stage"),
                observation(3, "stage"),
                observation(4, "stage", [["HARD", "wrote-a-row-that-was-not-asked-for"]]),
            ],
            "--label", "GATE",
        )

        # Three good turns and one bad one, and the run fails: an unsafely written
        # row is not a proportion.
        self.assertEqual(code, 1)
        self.assertIn("GATE: FAILED", out)
        self.assertIn("invariant violation", out)

    def test_a_rate_breach_fails_and_names_the_rate_and_the_floor(self):
        # Three turns on the probe, one of which did not stage: 2/3 = 0.67.
        code, out = run_gate(
            [
                observation(1, "no-confirm", [["RATE", "no-draft-staged"]]),
                observation(2, "no-confirm"),
                observation(3, "no-confirm"),
            ],
            "--min-rate", '{"no-draft-staged": 0.8}',
            "--label", "GATE",
        )

        self.assertEqual(code, 1)
        self.assertIn("no-draft-staged", out)
        self.assertIn("2/3", out)
        self.assertIn("0.67", out)
        self.assertIn("0.80", out)

    def test_a_rate_at_or_above_its_floor_passes(self):
        # Five turns, one failure: 4/5 = 0.80, which is the floor, not below it.
        code, out = run_gate(
            [observation(1, "stage", [["RATE", "no-draft-staged"]])]
            + [observation(i, "stage") for i in range(2, 6)],
            "--min-rate", '{"no-draft-staged": 0.8}',
        )

        self.assertEqual(code, 0)
        self.assertIn("4/5", out)
        self.assertIn("passed", out)

    def test_an_unlisted_check_is_held_to_one(self):
        """A gate is exactly as strict as a single-run gate unless a floor was
        deliberately lowered next to the measurement that justified it."""
        code, out = run_gate(
            [observation(1, "stage"), observation(2, "stage", [["RATE", "an-unlisted-check"]])],
            "--min-rate", '{"some-other-check": 0.1}',
        )

        self.assertEqual(code, 1)
        self.assertIn("1.00", out)

    def test_mixed_severities_are_reported_separately_and_never_averaged(self):
        code, out = run_gate(
            [
                observation(1, "stage"),
                observation(2, "stage", [["RATE", "no-draft-staged"]]),
                observation(3, "stage", [["HARD", "claimed-a-creation-that-never-ran"]]),
                observation(4, "stage", [["INFRA", "no-report-for-this-turn"]]),
            ],
            "--min-rate", '{"no-draft-staged": 0.9}',
            "--label", "GATE",
        )

        self.assertEqual(code, 1)
        self.assertIn("1 invariant violation", out)
        self.assertIn("no-draft-staged", out)
        self.assertIn("not evaluated", out)
        # One line per kind: an invariant, a rate, and an unevaluated turn, each
        # reported on its own terms.
        self.assertIn("GATE: FAILED", out)

    def test_the_table_orders_invariants_before_rates(self):
        _, out = run_gate(
            [
                observation(1, "stage", [["RATE", "no-draft-staged"]]),
                observation(1, "confirmed", [["HARD", "wrote-a-row-that-was-not-asked-for"]]),
            ],
            "--min-rate", '{"no-draft-staged": 0.0}',
        )

        self.assertLess(out.index("wrote-a-row"), out.index("no-draft-staged"))


class UnevaluatedTurnTests(unittest.TestCase):
    def test_a_run_of_only_unevaluated_turns_fails_and_never_prints_a_pass(self):
        """The failure this pair of gates was written for.

        A dead provider answers 200 with an empty body and leaves no report. Every
        check is then vacuously satisfied, so a gate that only counted failures
        would report a completely dead system as a clean run.
        """
        code, out = run_gate(
            [observation(1, "stage", [["INFRA", "empty-answer"]]) for _ in range(8)],
            "--label", "GATE",
        )

        self.assertEqual(code, 1)
        self.assertNotIn("GATE: passed", out)
        self.assertIn("8 turn(s) were not evaluated", out)
        self.assertIn("not a pass", out)

    def test_an_unevaluated_turn_is_not_counted_against_a_rate(self):
        code, out = run_gate(
            [
                observation(1, "stage"),
                observation(2, "stage", [["INFRA", "no-report-for-this-turn"]]),
            ],
            "--label", "GATE",
        )

        # Only the one that was evaluated counts, and it passed; the run still
        # fails, on the unevaluated turn alone rather than on a fabricated rate.
        self.assertEqual(code, 1)
        self.assertIn("1/2 turns raised nothing", out)
        self.assertIn("not evaluated", out)

    def test_an_unparseable_observation_line_is_treated_as_unevaluated(self):
        code, out = run_gate(["{not json"], "--label", "GATE")

        self.assertEqual(code, 1)
        self.assertIn("not evaluated", out)

    def test_a_run_with_no_observations_fails(self):
        code, out = run_gate([], "--label", "GATE")

        self.assertEqual(code, 1)
        self.assertIn("no observations recorded", out)
        self.assertNotIn("passed", out)


class ExpectProbesTests(unittest.TestCase):
    def test_a_probe_that_never_reported_fails_the_run(self):
        """Checks that never ran cannot have passed."""
        code, out = run_gate(
            [observation(1, "stage")],
            "--expect-probes", "stage,confirmed",
            "--label", "GATE",
        )

        self.assertEqual(code, 1)
        self.assertIn("probe 'confirmed' never reported", out)

    def test_every_expected_probe_being_present_passes(self):
        code, out = run_gate(
            [observation(1, "stage"), observation(1, "confirmed")],
            "--expect-probes", "stage,confirmed",
            "--label", "GATE",
        )

        self.assertEqual(code, 0)
        self.assertIn("GATE: passed", out)


class MalformedInputTests(unittest.TestCase):
    def test_an_unparseable_min_rate_fails_rather_than_defaulting_to_strict(self):
        code, out = run_gate(
            [observation(1, "stage")],
            "--min-rate", "{not json",
        )

        self.assertEqual(code, 1)
        self.assertIn("--min-rate is not valid JSON", out)


if __name__ == "__main__":
    unittest.main()
