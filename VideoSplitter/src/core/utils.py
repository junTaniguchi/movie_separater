from __future__ import annotations

import json
import locale
import logging
import os
import shlex
import subprocess
import sys
import threading
from pathlib import Path
from typing import Iterable, Optional

GB_IN_BYTES = 1024 ** 3
AUDIO_BITRATE = 128_000  # bits per second
MIN_VIDEO_BITRATE = 200_000  # bits per second


class OperationCancelled(RuntimeError):
    """Raised when a cancellation request is honoured."""


def get_runtime_dir() -> Path:
    """Return the directory that should contain runtime artefacts."""
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parents[2]


def get_default_output_dir() -> Path:
    """Return the default output directory for split files."""
    return get_runtime_dir() / "split"


def get_logs_dir() -> Path:
    """Return the directory used for log files."""
    return get_runtime_dir() / "logs"


def get_settings_path() -> Path:
    """Return the path to the settings file."""
    return get_runtime_dir() / "settings.json"


def ensure_directory(path: Path) -> Path:
    """Ensure directory exists and return it."""
    path.mkdir(parents=True, exist_ok=True)
    return path


def load_json(path: Path) -> dict:
    """Load a JSON file, returning an empty dict if missing or invalid."""
    if not path.exists():
        return {}
    try:
        with path.open("r", encoding="utf-8") as handle:
            return json.load(handle)
    except json.JSONDecodeError:
        return {}


def save_json(path: Path, data: dict) -> None:
    """Persist JSON data atomically."""
    ensure_directory(path.parent)
    tmp_path = path.with_suffix(".tmp")
    with tmp_path.open("w", encoding="utf-8") as handle:
        json.dump(data, handle, indent=2, ensure_ascii=False)
    tmp_path.replace(path)


def bytes_to_gb(value: int) -> float:
    """Convert bytes to gigabytes."""
    return value / GB_IN_BYTES


def gb_to_bytes(value: float) -> int:
    """Convert gigabytes to bytes."""
    return int(value * GB_IN_BYTES)


def human_readable_size(num_bytes: int) -> str:
    """Return human-readable size string."""
    step = 1024
    units = ["B", "KB", "MB", "GB", "TB", "PB"]
    size = float(num_bytes)
    for unit in units[:-1]:
        if size < step:
            return f"{size:.1f} {unit}"
        size /= step
    return f"{size:.1f} {units[-1]}"


def format_command(args: Iterable[str]) -> str:
    """Format command arguments for logging."""
    return " ".join(shlex.quote(str(a)) for a in args)


def run_command(
    args: Iterable[str],
    logger: logging.Logger,
    *,
    capture_output: bool = False,
    cancel_event: Optional[threading.Event] = None,
    cwd: Optional[Path] = None,
    stream_level: int = logging.INFO,
    text_encoding: Optional[str] = None,
) -> Optional[str]:
    """
    Execute a subprocess command.

    When capture_output is False the command output is streamed to the provided
    logger. Otherwise stdout is returned as a string while stderr is still
    echoed to the log.
    """
    args = [str(a) for a in args]
    logger.debug("Running command: %s", format_command(args))

    if capture_output:
        completed = subprocess.run(
            args,
            check=False,
            capture_output=True,
            cwd=str(cwd) if cwd else None,
        )
        encoding = text_encoding or locale.getpreferredencoding(False)
        stdout_bytes = completed.stdout or b""
        stderr_bytes = completed.stderr or b""
        try:
            stdout_text = stdout_bytes.decode(
                encoding, errors="strict" if text_encoding else "replace"
            )
        except UnicodeDecodeError as exc:
            raise RuntimeError(
                f"Failed to decode stdout from command: {format_command(args)}"
            ) from exc
        stderr_text = stderr_bytes.decode(encoding, errors="replace")

        if stdout_text:
            for line in stdout_text.splitlines():
                logger.log(stream_level, line)
        if stderr_text:
            for line in stderr_text.splitlines():
                logger.error(line)
        if completed.returncode != 0:
            raise subprocess.CalledProcessError(completed.returncode, args)
        return stdout_text

    with subprocess.Popen(
        args,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding=text_encoding or None,
        errors="replace",
        cwd=str(cwd) if cwd else None,
        bufsize=1,
    ) as proc:
        assert proc.stdout is not None
        for line in proc.stdout:
            logger.log(stream_level, line.rstrip())
            if cancel_event and cancel_event.is_set():
                logger.warning("Cancellation requested; terminating process.")
                proc.terminate()
                try:
                    proc.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    proc.kill()
                raise OperationCancelled("Process was cancelled by user.")
        returncode = proc.wait()
        if returncode != 0:
            raise subprocess.CalledProcessError(returncode, args)
    return None
