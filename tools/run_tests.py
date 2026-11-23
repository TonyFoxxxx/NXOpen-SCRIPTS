"""Run project tests once in the background and retain stdout, stderr and tracebacks."""
import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import signal
import subprocess
import sys
import tempfile
import traceback
import uuid


def run_posix(command, log, seconds):
    process = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=log, stderr=log,
                               start_new_session=True)
    try:
        return process.wait(timeout=seconds)
    finally:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--timeout", type=int, default=480, metavar="SECONDS")
    parser.add_argument("--log", type=Path, help="Diagnostic log file (default: a unique file in the temp directory)")
    parser.add_argument("command", nargs=argparse.REMAINDER, help="Optional executable and arguments after --")
    args = parser.parse_args()
    if not 1 <= args.timeout <= 86400:
        parser.error("timeout must be from 1 to 86400 seconds")
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        tests = Path(__file__).resolve().parents[1] / "tests"
        command = [sys.executable, "-X", "faulthandler", "-m", "unittest", "discover", "-s", str(tests), "-v", "-f"]
    now = datetime.now(timezone.utc)
    path = args.log or (Path(tempfile.gettempdir()) / "NXOpen-SCRIPTS" / "test-logs" /
                        (now.strftime("%Y%m%d-%H%M%S") + "-" + uuid.uuid4().hex[:12] + ".log"))
    path = path.resolve()
    path.parent.mkdir(parents=True, exist_ok=True)
    print("Test log:", path, flush=True)
    # Append also preserves earlier diagnostics if a caller explicitly reuses a name.
    with path.open("ab", buffering=0) as log:
        log.write(("\n" + now.isoformat() + "  " + repr(command) + "\n").encode("utf-8"))
        try:
            os.environ["PYTHONFAULTHANDLER"] = "1"
            os.environ["PYTHONIOENCODING"] = "utf-8"
            os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
            os.environ["NXOPEN_TEST_LOG_DIR"] = str(path.parent)
            if os.name == "nt":
                from _windows_test_process import run
            else:
                run = run_posix
            code = run(command, log, args.timeout)
        except subprocess.TimeoutExpired:
            log.write(("Timed out after %s seconds. Terminated the test process tree.\n" % args.timeout).encode())
            code = 124
        except (OSError, KeyboardInterrupt):
            log.write(traceback.format_exc().encode("utf-8", "backslashreplace"))
            code = 1
        log.write(("Exit code %d\n" % code).encode())
    if code:
        sys.stderr.buffer.write(path.read_bytes()[-12000:])
    return code if 0 <= code <= 255 else 1


if __name__ == "__main__":
    sys.exit(main())
