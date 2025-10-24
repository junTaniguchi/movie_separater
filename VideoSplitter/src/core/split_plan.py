from __future__ import annotations

import math
from typing import Dict

from .utils import GB_IN_BYTES


def make_plan(
    duration_sec: float, size_bytes: int, max_gb: float, max_min: float
) -> Dict[str, float]:
    """
    Derive the number of output parts and target segment duration.

    Returns a dict with 'parts' and 'segment_time'.
    """
    if duration_sec <= 0:
        raise ValueError("Video duration must be positive.")
    if size_bytes <= 0:
        raise ValueError("Video size must be positive.")
    if max_gb <= 0:
        raise ValueError("Maximum size (GB) must be greater than zero.")
    if max_min <= 0:
        raise ValueError("Maximum duration (minutes) must be greater than zero.")

    max_bytes = max_gb * GB_IN_BYTES
    max_seconds = max_min * 60.0

    size_parts = math.ceil(size_bytes / max_bytes) if max_bytes > 0 else 1
    time_parts = math.ceil(duration_sec / max_seconds) if max_seconds > 0 else 1
    parts = max(size_parts, time_parts, 1)

    segment_time = max(duration_sec / parts, 1.0)
    return {"parts": parts, "segment_time": segment_time}

