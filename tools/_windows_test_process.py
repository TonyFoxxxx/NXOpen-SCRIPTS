"""Start one command on a private desktop, bounded by a kill-on-close Windows job."""
import contextlib
import ctypes as C
from ctypes import wintypes as W
import msvcrt
import os
import shutil
import subprocess
import uuid


def api(dll, name, result, *args):
    fn = getattr(dll, name)
    fn.restype = result
    fn.argtypes = args
    return fn


class Startup(C.Structure):
    _fields_ = [
        ("cb", W.DWORD), ("lpReserved", W.LPWSTR), ("lpDesktop", W.LPWSTR), ("lpTitle", W.LPWSTR),
        ("dwX", W.DWORD), ("dwY", W.DWORD), ("dwXSize", W.DWORD), ("dwYSize", W.DWORD),
        ("dwXCountChars", W.DWORD), ("dwYCountChars", W.DWORD), ("dwFillAttribute", W.DWORD),
        ("dwFlags", W.DWORD), ("wShowWindow", W.WORD), ("cbReserved2", W.WORD),
        ("lpReserved2", W.LPVOID), ("hStdInput", W.HANDLE), ("hStdOutput", W.HANDLE), ("hStdError", W.HANDLE),
    ]


class Process(C.Structure):
    _fields_ = [("handle", W.HANDLE), ("thread", W.HANDLE), ("pid", W.DWORD), ("tid", W.DWORD)]


class BasicLimits(C.Structure):
    _fields_ = [("processTime", C.c_int64), ("jobTime", C.c_int64), ("flags", W.DWORD),
                ("minWorkingSet", C.c_size_t), ("maxWorkingSet", C.c_size_t), ("active", W.DWORD),
                ("affinity", C.c_size_t), ("priority", W.DWORD), ("scheduling", W.DWORD)]


class ExtendedLimits(C.Structure):
    _fields_ = [("basic", BasicLimits), ("ioCounters", C.c_uint64 * 6), ("processMemory", C.c_size_t),
                ("jobMemory", C.c_size_t), ("peakProcess", C.c_size_t), ("peakJob", C.c_size_t)]


kernel = C.WinDLL("kernel32", use_last_error=True)
user = C.WinDLL("user32", use_last_error=True)
create_desktop = api(user, "CreateDesktopW", W.HANDLE, W.LPCWSTR, W.LPCWSTR, W.LPVOID, W.DWORD, W.DWORD, W.LPVOID)
close_desktop = api(user, "CloseDesktop", W.BOOL, W.HANDLE)
create_job = api(kernel, "CreateJobObjectW", W.HANDLE, W.LPVOID, W.LPCWSTR)
set_job = api(kernel, "SetInformationJobObject", W.BOOL, W.HANDLE, C.c_int, W.LPVOID, W.DWORD)
assign = api(kernel, "AssignProcessToJobObject", W.BOOL, W.HANDLE, W.HANDLE)
close = api(kernel, "CloseHandle", W.BOOL, W.HANDLE)
create = api(kernel, "CreateProcessW", W.BOOL, W.LPCWSTR, W.LPWSTR, W.LPVOID, W.LPVOID, W.BOOL,
             W.DWORD, W.LPVOID, W.LPCWSTR, C.POINTER(Startup), C.POINTER(Process))
resume = api(kernel, "ResumeThread", W.DWORD, W.HANDLE)
wait = api(kernel, "WaitForSingleObject", W.DWORD, W.HANDLE, W.DWORD)
exit_code = api(kernel, "GetExitCodeProcess", W.BOOL, W.HANDLE, C.POINTER(W.DWORD))
terminate = api(kernel, "TerminateProcess", W.BOOL, W.HANDLE, W.UINT)
terminate_job = api(kernel, "TerminateJobObject", W.BOOL, W.HANDLE, W.UINT)


def checked(value):
    if not value:
        raise C.WinError(C.get_last_error())
    return value


def run(command, log, seconds):
    application = shutil.which(command[0])
    if not application:
        raise FileNotFoundError(command[0])
    name = "NXOpenTests_" + uuid.uuid4().hex
    with contextlib.ExitStack() as stack:
        # This desktop is never switched to the input desktop.
        desktop = checked(create_desktop(name, None, None, 0, 0x01FF, None))
        stack.callback(close_desktop, desktop)
        job = checked(create_job(None, None))
        stack.callback(close, job)
        limits = ExtendedLimits()
        limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        checked(set_job(job, 9, C.byref(limits), C.sizeof(limits)))

        stdin = stack.enter_context(open(os.devnull, "rb"))
        startup = Startup()
        startup.cb = C.sizeof(startup)
        startup.lpDesktop = name
        # The desktop itself is private. Allow UI tests to show controls within it.
        startup.dwFlags = 0x100  # STARTF_USESTDHANDLES
        startup.hStdInput = msvcrt.get_osfhandle(stdin.fileno())
        startup.hStdOutput = startup.hStdError = msvcrt.get_osfhandle(log.fileno())
        process = Process()
        cmdline = C.create_unicode_buffer(subprocess.list2cmdline(command))
        log.write(("Desktop " + name + "\n").encode())
        log.flush()
        with contextlib.ExitStack() as inheritable:
            for handle in (startup.hStdInput, startup.hStdOutput):
                inheritable.callback(os.set_handle_inheritable, handle, os.get_handle_inheritable(handle))
                os.set_handle_inheritable(handle, True)
            # The test cannot run before its entire descendant tree is contained.
            checked(create(application, cmdline, None, None, True, 0x08000004, None, None,
                           C.byref(startup), C.byref(process)))  # CREATE_NO_WINDOW | CREATE_SUSPENDED
        stack.callback(close, process.handle)
        stack.callback(close, process.thread)
        if not assign(job, process.handle):
            error = C.WinError(C.get_last_error())
            terminate(process.handle, 1)
            wait(process.handle, 5000)
            raise error
        if resume(process.thread) == 0xFFFFFFFF:
            raise C.WinError(C.get_last_error())
        result = wait(process.handle, seconds * 1000)
        if result == 258:  # WAIT_TIMEOUT
            checked(terminate_job(job, 124))
            wait(process.handle, 5000)
            raise subprocess.TimeoutExpired(command, seconds)
        if result != 0:
            raise C.WinError(C.get_last_error())
        status = W.DWORD()
        checked(exit_code(process.handle, C.byref(status)))
        return status.value
