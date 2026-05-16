#!/usr/bin/env python3
"""Terminal-driven E2E smoke test for v3 CLI recovery and readiness."""

import argparse
import json
import os
import platform
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from typing import Any


SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(SCRIPT_DIR)
STATE_RELATIVE_PATH = os.path.join("Temp", "UnityCliLoop", "server-state.json")
EXPECTED_DYNAMIC_CODE_RESULT = "cli-recovery-readiness-e2e"
E2E_DYNAMIC_CODE = f'return "{EXPECTED_DYNAMIC_CODE_RESULT}";'


@dataclass(frozen=True)
class CommandResult:
    args: list[str]
    returncode: int
    stdout: str
    stderr: str
    elapsed: float
    timed_out: bool


def default_uloop_path() -> str:
    env_path = os.environ.get("ULOOP_BIN", "")
    if env_path:
        return env_path

    if sys.platform == "darwin":
        machine = platform.machine().lower()
        arch = "darwin-arm64" if machine in ("arm64", "aarch64") else "darwin-amd64"
        return os.path.join(REPO_ROOT, "Packages", "src", "Cli~", "dist", arch, "uloop")

    if sys.platform == "win32":
        return os.path.join(REPO_ROOT, "Packages", "src", "Cli~", "dist", "windows-amd64", "uloop.exe")

    return ""


def run_command(args: list[str], cwd: str, timeout: float) -> CommandResult:
    started_at = time.monotonic()
    try:
        result = subprocess.run(
            args,
            cwd=cwd,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=timeout,
        )
        return CommandResult(
            args=args,
            returncode=result.returncode,
            stdout=result.stdout,
            stderr=result.stderr,
            elapsed=time.monotonic() - started_at,
            timed_out=False,
        )
    except subprocess.TimeoutExpired as err:
        stdout = decode_timeout_output(err.stdout)
        stderr = decode_timeout_output(err.stderr)
        return CommandResult(
            args=args,
            returncode=124,
            stdout=stdout,
            stderr=stderr,
            elapsed=time.monotonic() - started_at,
            timed_out=True,
        )


def decode_timeout_output(output: Any) -> str:
    if output is None:
        return ""
    if isinstance(output, bytes):
        return output.decode("utf-8", "replace")
    return str(output)


def run_uloop(uloop_path: str, project_path: str, args: list[str], timeout: float) -> CommandResult:
    command = [uloop_path, "--project-path", project_path, *args]
    return run_command(command, project_path, timeout)


def assert_success(result: CommandResult, label: str) -> None:
    if result.returncode == 0 and not result.timed_out:
        print(f"{label} passed in {result.elapsed:.1f}s")
        return

    print_command_context(label, result)
    raise AssertionError(label)


def print_command_context(label: str, result: CommandResult) -> None:
    print(f"{label} failed")
    print(f"command: {format_command(result.args)}")
    print(f"exit_code: {result.returncode}")
    print(f"elapsed: {result.elapsed:.1f}s")
    print(f"timed_out: {result.timed_out}")
    print("--- stdout ---")
    print(result.stdout)
    print("--- stderr ---")
    print(result.stderr)


def format_command(args: list[str]) -> str:
    return " ".join(args)


def assert_json_success(result: CommandResult, label: str) -> dict[str, Any]:
    assert_success(result, label)
    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError as err:
        print_command_context(label, result)
        raise AssertionError(f"{label} did not return JSON: {err}") from err

    if payload.get("Success") is False:
        print_command_context(label, result)
        raise AssertionError(f"{label} returned Success=false")

    return payload


def assert_dynamic_code_result(payload: dict[str, Any]) -> None:
    if payload.get("Result") == EXPECTED_DYNAMIC_CODE_RESULT:
        return
    raise AssertionError(f"execute-dynamic-code result mismatch: {payload}")


def assert_stale_recovery_state_error(result: CommandResult) -> None:
    if result.returncode == 0 or result.timed_out:
        print_command_context("stale recovery-state check", result)
        raise AssertionError("stale recovery-state check should fail without timing out")

    combined_output = result.stdout + result.stderr
    required_fragments = [
        "stale Unity CLI Loop recovery state file",
        "Run `uloop fix` to remove stale recovery state files.",
    ]
    for fragment in required_fragments:
        if fragment not in combined_output:
            print_command_context("stale recovery-state check", result)
            raise AssertionError(f"stale recovery-state output missing: {fragment}")

    print(f"stale recovery-state check passed in {result.elapsed:.1f}s")


def run_live_recovery_sequence(uloop_path: str, project_path: str, timeout: float, launch_timeout: float) -> None:
    assert_success(
        run_uloop(uloop_path, project_path, ["launch"], launch_timeout),
        "launch or reuse Unity",
    )
    assert_json_success(
        run_uloop(uloop_path, project_path, ["get-logs", "--max-count", "1"], timeout),
        "initial get-logs readiness check",
    )
    assert_json_success(
        run_uloop(uloop_path, project_path, ["compile", "--wait-for-domain-reload"], timeout),
        "compile with domain reload wait",
    )
    assert_json_success(
        run_uloop(uloop_path, project_path, ["get-logs", "--max-count", "1"], timeout),
        "immediate get-logs after compile",
    )
    dynamic_payload = assert_json_success(
        run_uloop(
            uloop_path,
            project_path,
            ["execute-dynamic-code", "--code", E2E_DYNAMIC_CODE],
            timeout,
        ),
        "execute-dynamic-code after recovery",
    )
    assert_dynamic_code_result(dynamic_payload)


def run_stale_recovery_state_sequence(uloop_path: str, timeout: float) -> None:
    with tempfile.TemporaryDirectory(prefix="uloop-stale-state-") as project_path:
        create_minimal_unity_project(project_path)
        write_stale_server_state(project_path)
        print(f"stale_state_project={project_path}")

        stale_result = run_uloop(uloop_path, project_path, ["get-logs", "--max-count", "1"], timeout)
        assert_stale_recovery_state_error(stale_result)

        assert_success(
            run_uloop(uloop_path, project_path, ["fix"], timeout),
            "cleanup stale recovery state",
        )
        state_path = os.path.join(project_path, STATE_RELATIVE_PATH)
        if os.path.exists(state_path):
            raise AssertionError(f"stale recovery state was not removed: {state_path}")


def create_minimal_unity_project(project_path: str) -> None:
    os.makedirs(os.path.join(project_path, "Assets"), exist_ok=True)
    os.makedirs(os.path.join(project_path, "ProjectSettings"), exist_ok=True)
    with open(os.path.join(project_path, "ProjectSettings", "ProjectVersion.txt"), "w", encoding="utf-8") as file:
        file.write("m_EditorVersion: 6000.0.0f1\n")


def write_stale_server_state(project_path: str) -> None:
    state_path = os.path.join(project_path, STATE_RELATIVE_PATH)
    os.makedirs(os.path.dirname(state_path), exist_ok=True)
    state = {
        "phase": "recovering",
        "generationId": "stale-e2e",
        "updatedAt": "1970-01-01T00:00:00Z",
        "reason": "domain-reload-after",
        "endpoint": "stale-e2e",
        "lastError": "",
    }
    with open(state_path, "w", encoding="utf-8") as file:
        json.dump(state, file)


def validate_paths(project_path: str, uloop_path: str) -> None:
    if not os.path.isdir(os.path.join(project_path, "Assets")):
        raise AssertionError(f"--project-path does not contain Assets: {project_path}")
    if not os.path.isdir(os.path.join(project_path, "ProjectSettings")):
        raise AssertionError(f"--project-path does not contain ProjectSettings: {project_path}")
    if not uloop_path:
        raise AssertionError("No checked-in uloop binary is available for this platform. Pass --uloop-path.")
    if not os.path.isfile(uloop_path):
        raise AssertionError(f"uloop binary not found: {uloop_path}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-path", required=True)
    parser.add_argument("--uloop-path", default=default_uloop_path())
    parser.add_argument("--timeout", type=float, default=120.0)
    parser.add_argument("--launch-timeout", type=float, default=240.0)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    project_path = os.path.abspath(args.project_path)
    uloop_path = os.path.abspath(args.uloop_path)
    validate_paths(project_path, uloop_path)

    print("=== CLI recovery/readiness smoke ===")
    print(f"project_path={project_path}")
    print(f"uloop_path={uloop_path}")

    run_live_recovery_sequence(uloop_path, project_path, args.timeout, args.launch_timeout)
    run_stale_recovery_state_sequence(uloop_path, args.timeout)

    print("CLI recovery/readiness smoke passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
