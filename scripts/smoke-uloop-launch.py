#!/usr/bin/env python3
"""Smoke test real uloop launch behavior through a pseudo terminal."""

import argparse
import os
import pty
import select
import signal
import subprocess
import sys
import time


def run_command(command: list[str], project_path: str, timeout: float) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=project_path,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        timeout=timeout,
    )


def run_pty(
    command: list[str],
    project_path: str,
    timeout: float,
    interrupt_after: float | None = None,
) -> tuple[int | None, str, float, str]:
    master, slave = pty.openpty()
    started_at = time.time()
    process = subprocess.Popen(
        command,
        cwd=project_path,
        stdin=slave,
        stdout=slave,
        stderr=slave,
        start_new_session=True,
    )
    os.close(slave)
    output = bytearray()
    interrupted = False

    try:
        while True:
            elapsed = time.time() - started_at
            if interrupt_after is not None and not interrupted and elapsed >= interrupt_after:
                os.killpg(process.pid, signal.SIGINT)
                interrupted = True

            if elapsed > timeout:
                terminate_process_group(process.pid)
                return process.poll(), output.decode("utf-8", "replace"), elapsed, "timeout"

            read_available_output(master, output, 0.1)

            exit_code = process.poll()
            if exit_code is None:
                continue

            wait_for_quiet_output(master, output)
            return exit_code, output.decode("utf-8", "replace"), time.time() - started_at, "exited"
    finally:
        os.close(master)


def read_available_output(master: int, output: bytearray, timeout: float) -> None:
    readable, _, _ = select.select([master], [], [], timeout)
    if master not in readable:
        return

    try:
        data = os.read(master, 4096)
    except OSError:
        return
    output.extend(data)


def wait_for_quiet_output(master: int, output: bytearray) -> None:
    quiet_started_at = time.time()
    while time.time() - quiet_started_at < 1.0:
        before = len(output)
        read_available_output(master, output, 0.1)
        if len(output) != before:
            quiet_started_at = time.time()


def terminate_process_group(pid: int) -> None:
    try:
        os.killpg(pid, signal.SIGTERM)
    except ProcessLookupError:
        return


def assert_normal_launch(project_path: str, timeout: float) -> None:
    exit_code, output, elapsed, status = run_pty(["uloop", "launch"], project_path, timeout)
    print(f"normal launch: status={status} code={exit_code} elapsed={elapsed:.1f}s")

    if status != "exited" or exit_code != 0:
        print(output)
        raise AssertionError("uloop launch did not finish successfully")
    if "Waiting for Unity to finish starting" not in output:
        raise AssertionError("uloop launch did not render the startup spinner")
    if not output.endswith("\r\x1b[K\r\n"):
        print(repr(output[-300:]))
        raise AssertionError("uloop launch did not clear the spinner line before exit")


def assert_dynamic_code(project_path: str, expected: str, timeout: float) -> None:
    result = run_command(
        [
            "uloop",
            "execute-dynamic-code",
            "--project-path",
            project_path,
            "--code",
            f'return "{expected}";',
        ],
        project_path,
        timeout,
    )
    print(result.stdout)
    if result.returncode != 0 or expected not in result.stdout:
        raise AssertionError("execute-dynamic-code did not become ready")


def assert_interrupt_keeps_unity_alive(project_path: str, timeout: float) -> None:
    exit_code, output, elapsed, status = run_pty(
        ["uloop", "launch"],
        project_path,
        timeout,
        interrupt_after=2.0,
    )
    print(f"interrupted launch: status={status} code={exit_code} elapsed={elapsed:.1f}s")
    if status != "exited":
        print(output)
        raise AssertionError("interrupted uloop launch did not exit")

    for attempt in range(1, 31):
        try:
            result = run_command(
                [
                    "uloop",
                    "execute-dynamic-code",
                    "--project-path",
                    project_path,
                    "--code",
                    'return "alive-after-ctrl-c";',
                ],
                project_path,
                10,
            )
        except subprocess.TimeoutExpired:
            print(f"Unity reachability check timed out: attempt={attempt}")
            time.sleep(1)
            continue

        if result.returncode == 0 and "alive-after-ctrl-c" in result.stdout:
            print(f"Unity stayed alive after Ctrl+C: attempt={attempt}")
            return
        time.sleep(1)

    raise AssertionError("Unity did not stay reachable after Ctrl+C")


def quit_unity(project_path: str) -> None:
    result = run_command(["uloop", "launch", "-q"], project_path, 30)
    print(result.stdout)
    if result.returncode != 0:
        raise AssertionError("failed to quit Unity before smoke test")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project-path", default=os.getcwd())
    parser.add_argument("--timeout", type=float, default=90.0)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    project_path = os.path.abspath(args.project_path)

    quit_unity(project_path)
    assert_normal_launch(project_path, args.timeout)
    assert_dynamic_code(project_path, "ready-after-smoke-launch", 20)

    quit_unity(project_path)
    assert_interrupt_keeps_unity_alive(project_path, args.timeout)

    print("uloop launch smoke test passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
