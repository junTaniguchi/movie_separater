from __future__ import annotations

import json
import logging
from pathlib import Path
from typing import Any, Dict

from .ffmpeg_locator import get_ffmpeg_paths
from .utils import run_command


logger = logging.getLogger("VideoSplitter")


def _parse_float(value: Any, default: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def _parse_int(value: Any, default: int = 0) -> int:
    try:
        return int(float(value))
    except (TypeError, ValueError):
        return default


def probe(input_path: Path) -> Dict[str, Any]:
    """Run ffprobe on the given file and return metadata."""
    if not Path(input_path).exists():
        raise FileNotFoundError(f"Input file not found: {input_path}")

    _, ffprobe_path = get_ffmpeg_paths()

    cmd = [
        str(ffprobe_path),
        "-v",
        "error",
        "-print_format",
        "json",
        "-show_format",
        "-show_streams",
        str(input_path),
    ]

    output = run_command(cmd, logger, capture_output=True, stream_level=logging.DEBUG)
    assert output is not None

    try:
        info = json.loads(output)
    except json.JSONDecodeError as exc:
        raise RuntimeError("ffprobe returned invalid JSON.") from exc

    fmt = info.get("format", {})
    streams = info.get("streams", [])

    duration = _parse_float(fmt.get("duration"))
    size_bytes = _parse_int(fmt.get("size"))
    bit_rate = _parse_int(fmt.get("bit_rate"))

    if bit_rate <= 0 and duration > 0 and size_bytes > 0:
        bit_rate = int((size_bytes * 8) / duration)

    if duration <= 0:
        for stream in streams:
            duration = _parse_float(stream.get("duration"))
            if duration > 0:
                break

    result = {
        "duration": duration,
        "size_bytes": size_bytes,
        "bit_rate": bit_rate,
        "format": fmt,
        "streams": streams,
    }
    logger.debug(
        "ffprobe metadata: duration=%.3fs, size=%d bytes, bitrate=%d bps",
        duration,
        size_bytes,
        bit_rate,
    )
    return result

