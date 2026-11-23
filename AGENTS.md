# Automated tests

Run all automated project tests through `python tools/run_tests.py`. This also applies to individual fixtures, compiler probes and temporary regression executables. Pass a custom command after `--`. The runner records the log path at startup. By default logs go to `%TEMP%\NXOpen-SCRIPTS\test-logs` on Windows.

- On Windows, start the runner from the existing background task. It starts test processes on a private desktop and gives them a deadline. If isolation fails, stop the local run or use hosted CI.
- Catch exceptions at managed test entry points and in worker threads. Use `tests/fixtures/ManagedTestHost.cs` for C# fixtures. Record the exception type, message and stack and return a failing exit code.
- Run each failing command once. Diagnose the recorded failure before starting a corrected test. On timeout or cancellation, terminate the owned test process tree. Leave unrelated processes and the interactive NX session alone.
- Keep Windows Error Reporting and system diagnostics enabled. Confine any UI suppression to the test process. Keep the workstation's registry, services, debugger settings and system error policies intact.
