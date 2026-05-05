#!/usr/bin/env python3
"""Smoke test compile --wait-for-domain-reload followed immediately by get-logs."""

import argparse
import os
import subprocess
import sys
import time


def run_command(
    command: list[str],
    project_path: str,
    timeout: float,
) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=project_path,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        timeout=timeout,
    )


def run_uloop(
    args: list[str],
    project_path: str,
    timeout: float,
) -> subprocess.CompletedProcess[str]:
    return run_command(["uloop", *args, "--project-path", project_path], project_path, timeout)


def assert_success(result: subprocess.CompletedProcess[str], label: str) -> None:
    if result.returncode == 0:
        return

    print(f"{label} failed with exit code {result.returncode}")
    print("--- stdout ---")
    print(result.stdout)
    print("--- stderr ---")
    print(result.stderr)
    raise AssertionError(label)


def assert_ready(project_path: str, timeout: float) -> None:
    result = run_uloop(["get-logs", "--max-count", "1"], project_path, timeout)
    assert_success(result, "initial get-logs readiness check")


def assert_compile_then_get_logs(project_path: str, timeout: float, round_index: int) -> None:
    compile_started_at = time.time()
    compile_result = run_uloop(
        [
            "compile",
            "--wait-for-domain-reload",
        ],
        project_path,
        timeout,
    )
    compile_elapsed = time.time() - compile_started_at
    assert_success(compile_result, f"round {round_index} compile")

    logs_started_at = time.time()
    logs_result = run_uloop(["get-logs", "--max-count", "1"], project_path, timeout)
    logs_elapsed = time.time() - logs_started_at
    assert_success(logs_result, f"round {round_index} immediate get-logs")

    print(
        f"round {round_index}: compile={compile_elapsed:.1f}s "
        f"immediate_get_logs={logs_elapsed:.1f}s"
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-path", default=".")
    parser.add_argument("--rounds", type=int, default=3)
    parser.add_argument("--timeout", type=float, default=90.0)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    project_path = os.path.abspath(args.project_path)

    assert_ready(project_path, args.timeout)
    for round_index in range(1, args.rounds + 1):
        assert_compile_then_get_logs(project_path, args.timeout, round_index)

    print("compile/get-logs smoke test passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
