from __future__ import annotations

import logging
from logging.handlers import RotatingFileHandler
from queue import Queue
from typing import Optional

from .utils import ensure_directory, get_logs_dir


class QueueHandler(logging.Handler):
    """Forward log records to a queue for the Tkinter UI."""

    def __init__(self, log_queue: Queue[str]) -> None:
        super().__init__()
        self.log_queue = log_queue

    def emit(self, record: logging.LogRecord) -> None:
        try:
            msg = self.format(record)
            self.log_queue.put_nowait(msg)
        except Exception:  # pragma: no cover - defensive
            self.handleError(record)


def setup_logging(log_queue: Queue[str], level: int = logging.INFO) -> logging.Logger:
    """Configure the application logger."""
    logger = logging.getLogger("VideoSplitter")
    logger.setLevel(level)
    logger.propagate = False
    logger.handlers.clear()

    formatter = logging.Formatter("%(asctime)s [%(levelname)s] %(message)s")

    logs_dir = ensure_directory(get_logs_dir())
    file_handler = RotatingFileHandler(
        logs_dir / "app.log",
        maxBytes=1_048_576,
        backupCount=3,
        encoding="utf-8",
    )
    file_handler.setFormatter(formatter)
    file_handler.setLevel(level)
    logger.addHandler(file_handler)

    queue_handler = QueueHandler(log_queue)
    queue_handler.setFormatter(formatter)
    queue_handler.setLevel(level)
    logger.addHandler(queue_handler)

    return logger

