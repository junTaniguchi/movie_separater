from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path
from typing import Iterable, Tuple


def _candidate_directories() -> Iterable[Path]:
    """Yield directories to search for FFmpeg binaries."""
    if hasattr(sys, "_MEIPASS"):
        yield Path(sys._MEIPASS)

    if getattr(sys, "frozen", False):
        yield Path(sys.executable).resolve().parent
    else:
        yield Path(__file__).resolve().parents[2]

    yield Path.cwd()
    yield Path(__file__).resolve().parents[2] / "third_party" / "ffmpeg" / "win-x64"


def get_ffmpeg_paths() -> Tuple[Path, Path]:
    """Locate ffmpeg and ffprobe binaries."""
    candidates = []
    for base_dir in _candidate_directories():
        ffmpeg_path = base_dir / "ffmpeg.exe"
        ffprobe_path = base_dir / "ffprobe.exe"
        if ffmpeg_path.exists() and ffprobe_path.exists():
            return ffmpeg_path.resolve(), ffprobe_path.resolve()
        candidates.append((ffmpeg_path, ffprobe_path))

    ffmpeg_from_path = shutil.which("ffmpeg")
    ffprobe_from_path = shutil.which("ffprobe")
    if ffmpeg_from_path and ffprobe_from_path:
        return Path(ffmpeg_from_path).resolve(), Path(ffprobe_from_path).resolve()

    search_details = "\n".join(
        f"- {ffmpeg.parent}" for ffmpeg, _ in candidates
    )
    raise FileNotFoundError(
        "ffmpeg.exe and ffprobe.exe could not be located. "
        "Ensure the binaries are bundled with the application or "
        "available on PATH. Searched:\n"
        f"{search_details}"
    )

