from __future__ import annotations

import logging
import threading
from pathlib import Path
from typing import Callable, Iterable, List, Optional

from . import utils
from .ffprobe import probe


ProgressCallback = Callable[[int, int], None]
DEFAULT_AUDIO_BITRATE = "192k"


def _check_cancel(cancel_event: Optional[threading.Event]) -> None:
    if cancel_event and cancel_event.is_set():
        raise utils.OperationCancelled("Operation cancelled.")


def copy_split(
    input_path: Path,
    out_dir: Path,
    segment_time: float,
    ffmpeg_path: Path,
    logger: logging.Logger,
    cancel_event: Optional[threading.Event] = None,
) -> List[Path]:
    """
    Split a video into segments using stream copying.
    """
    _check_cancel(cancel_event)

    out_dir = utils.ensure_directory(Path(out_dir))
    segment_time = max(segment_time, 1.0)
    base_name = input_path.stem
    output_pattern = out_dir / f"{base_name}_part_%02d.mp4"

    cmd = [
        str(ffmpeg_path),
        "-y",
        "-v",
        "error",
        "-i",
        str(input_path),
        "-c",
        "copy",
        "-map",
        "0",
        "-f",
        "segment",
        "-reset_timestamps",
        "1",
        "-segment_time",
        f"{segment_time:.3f}",
        str(output_pattern),
    ]

    utils.run_command(cmd, logger, cancel_event=cancel_event)

    created = sorted(out_dir.glob(f"{base_name}_part_*.mp4"))
    if not created:
        raise RuntimeError("ffmpeg did not produce any output parts.")

    logger.info("Created %d part(s) via copy split.", len(created))
    return created


def split_audio_file(
    input_path: Path,
    out_dir: Path,
    segment_time: float,
    ffmpeg_path: Path,
    logger: logging.Logger,
    *,
    output_suffix: Optional[str] = None,
    cancel_event: Optional[threading.Event] = None,
) -> List[Path]:
    """
    Split an audio file into parts using stream copy.
    """
    _check_cancel(cancel_event)

    out_dir = utils.ensure_directory(Path(out_dir))
    segment_time = max(segment_time, 1.0)
    base_name = input_path.stem
    suffix = (output_suffix or input_path.suffix or ".mp3").lower()
    if not suffix.startswith("."):
        suffix = f".{suffix}"
    output_pattern = out_dir / f"{base_name}_part_%02d{suffix}"

    cmd = [
        str(ffmpeg_path),
        "-y",
        "-v",
        "error",
        "-i",
        str(input_path),
        "-map",
        "0:a?",
        "-acodec",
        "copy",
        "-f",
        "segment",
        "-reset_timestamps",
        "1",
        "-segment_time",
        f"{segment_time:.3f}",
        str(output_pattern),
    ]

    utils.run_command(cmd, logger, cancel_event=cancel_event)

    created = sorted(out_dir.glob(f"{base_name}_part_*{suffix}"))
    if not created:
        raise RuntimeError("ffmpeg did not produce any output parts.")

    logger.info("Created %d audio part(s) via copy split.", len(created))
    return created


def _calculate_bitrate(
    duration: float, max_gb: float, safety_ratio: float = 0.95
) -> int:
    """Return target video bitrate in bits per second."""
    if duration <= 0:
        raise ValueError("Segment duration must be positive.")
    target_bytes = utils.gb_to_bytes(max_gb) * safety_ratio
    target_bits = max(target_bytes * 8, 1)
    video_bits = target_bits / duration - utils.AUDIO_BITRATE
    return max(int(video_bits), utils.MIN_VIDEO_BITRATE)


def _encode_with_target_bitrate(
    source: Path,
    destination: Path,
    ffmpeg_path: Path,
    logger: logging.Logger,
    *,
    video_bitrate: int,
    cancel_event: Optional[threading.Event] = None,
) -> None:
    """Encode using capped bitrate targeting."""
    bitrate_k = max(video_bitrate // 1000, 1)
    maxrate_k = int(bitrate_k * 1.1)
    bufsize_k = int(bitrate_k * 2)

    cmd = [
        str(ffmpeg_path),
        "-y",
        "-i",
        str(source),
        "-map",
        "0",
        "-c:v",
        "libx264",
        "-preset",
        "medium",
        "-b:v",
        f"{bitrate_k}k",
        "-maxrate",
        f"{maxrate_k}k",
        "-bufsize",
        f"{bufsize_k}k",
        "-c:a",
        "aac",
        "-b:a",
        "128k",
        "-movflags",
        "+faststart",
        str(destination),
    ]

    utils.run_command(cmd, logger, cancel_event=cancel_event)


def _encode_with_crf(
    source: Path,
    destination: Path,
    ffmpeg_path: Path,
    logger: logging.Logger,
    *,
    crf: int,
    cancel_event: Optional[threading.Event] = None,
) -> None:
    """Encode using CRF quality mode."""
    cmd = [
        str(ffmpeg_path),
        "-y",
        "-i",
        str(source),
        "-map",
        "0",
        "-c:v",
        "libx264",
        "-preset",
        "medium",
        "-crf",
        str(crf),
        "-c:a",
        "aac",
        "-b:a",
        "128k",
        "-movflags",
        "+faststart",
        str(destination),
    ]

    utils.run_command(cmd, logger, cancel_event=cancel_event)


def reencode_oversize(
    files: Iterable[Path],
    max_gb: float,
    ffmpeg_path: Path,
    logger: logging.Logger,
    *,
    cancel_event: Optional[threading.Event] = None,
    progress_callback: Optional[ProgressCallback] = None,
) -> List[Path]:
    """
    Re-encode parts that exceed the size threshold.

    Returns a list with the final files (re-encoded or original).
    """
    max_bytes = utils.gb_to_bytes(max_gb)
    final_files: List[Path] = []
    files_list = list(files)
    total = len(files_list)

    for index, file_path in enumerate(files_list, start=1):
        _check_cancel(cancel_event)

        size = file_path.stat().st_size
        if size <= max_bytes:
            logger.info(
                "Part %s within limits (%s).",
                file_path.name,
                utils.human_readable_size(size),
            )
            final_files.append(file_path)
            if progress_callback:
                progress_callback(index, total)
            continue

        logger.warning(
            "Part %s exceeds limit (%s > %.2f GB). Re-encoding...",
            file_path.name,
            utils.human_readable_size(size),
            max_gb,
        )

        meta = probe(file_path)
        duration = meta.get("duration", 0.0)
        if duration <= 0:
            logger.error("Unable to determine duration for %s; skipping.", file_path)
            final_files.append(file_path)
            if progress_callback:
                progress_callback(index, total)
            continue

        tmp_file = file_path.with_suffix(".tmp.mp4")
        if tmp_file.exists():
            tmp_file.unlink()

        try:
            target_bitrate = _calculate_bitrate(duration, max_gb)
        except ValueError as exc:
            logger.error(str(exc))
            final_files.append(file_path)
            if progress_callback:
                progress_callback(index, total)
            continue

        try:
            _encode_with_target_bitrate(
                file_path,
                tmp_file,
                ffmpeg_path,
                logger,
                video_bitrate=target_bitrate,
                cancel_event=cancel_event,
            )
        except utils.OperationCancelled:
            if tmp_file.exists():
                tmp_file.unlink()
            raise

        if not tmp_file.exists():
            logger.error("FFmpeg failed to create %s", tmp_file)
            final_files.append(file_path)
            if progress_callback:
                progress_callback(index, total)
            continue

        new_size = tmp_file.stat().st_size
        if new_size > max_bytes:
            logger.warning(
                "Re-encoded file still too large (%s). Trying CRF fallback.",
                utils.human_readable_size(new_size),
            )
            tmp_file.unlink(missing_ok=True)

            for crf in (23, 26, 28):
                _check_cancel(cancel_event)
                _encode_with_crf(
                    file_path,
                    tmp_file,
                    ffmpeg_path,
                    logger,
                    crf=crf,
                    cancel_event=cancel_event,
                )
                if not tmp_file.exists():
                    continue
                new_size = tmp_file.stat().st_size
                if new_size <= max_bytes:
                    logger.info(
                        "CRF %d produced acceptable size for %s (%s).",
                        crf,
                        file_path.name,
                        utils.human_readable_size(new_size),
                    )
                    break
                logger.warning(
                    "CRF %d still too large (%s).",
                    crf,
                    utils.human_readable_size(new_size),
                )
                tmp_file.unlink(missing_ok=True)

        if tmp_file.exists():
            tmp_file.replace(file_path)
            logger.info(
                "Re-encoded %s -> %s.",
                file_path.name,
                utils.human_readable_size(file_path.stat().st_size),
            )
        else:
            logger.warning(
                "Falling back to original oversize part %s.", file_path.name
            )

        final_files.append(file_path)
        if progress_callback:
            progress_callback(index, total)

    return final_files


def extract_audio(
    input_path: Path,
    output_path: Path,
    ffmpeg_path: Path,
    logger: logging.Logger,
    *,
    audio_bitrate: str = DEFAULT_AUDIO_BITRATE,
    cancel_event: Optional[threading.Event] = None,
) -> Path:
    """
    Extract audio track from a video file into a standalone MP3.
    """
    _check_cancel(cancel_event)
    utils.ensure_directory(output_path.parent)

    tmp_output = output_path.with_suffix(".tmp.mp3")
    tmp_output.unlink(missing_ok=True)

    cmd = [
        str(ffmpeg_path),
        "-y",
        "-i",
        str(input_path),
        "-vn",
        "-acodec",
        "libmp3lame",
        "-b:a",
        audio_bitrate,
        "-f",
        "mp3",
        str(tmp_output),
    ]

    utils.run_command(cmd, logger, cancel_event=cancel_event)

    if not tmp_output.exists():
        raise RuntimeError("音声ファイルの生成に失敗しました。")

    tmp_output.replace(output_path)
    logger.info("Extracted audio to %s", output_path)
    return output_path
