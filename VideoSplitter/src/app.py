from __future__ import annotations

import logging
import os
import queue
import subprocess
import sys
import threading
from pathlib import Path
from typing import List, Optional

import tkinter as tk
from tkinter import filedialog, messagebox, ttk
from tkinter.scrolledtext import ScrolledText

from core.ffmpeg_locator import get_ffmpeg_paths
from core.ffprobe import probe
from core.logging_setup import setup_logging
from core.split_plan import make_plan
from core.splitter import copy_split, reencode_oversize
from core import utils


class VideoSplitterApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("Video Splitter")
        self.geometry("780x540")
        self.minsize(720, 480)

        icon_path = Path(utils.get_runtime_dir()) / "assets" / "icon.ico"
        if icon_path.exists():
            try:
                self.iconbitmap(default=str(icon_path))
            except Exception:
                pass

        self.log_queue: "queue.Queue[str]" = queue.Queue()
        self.event_queue: "queue.Queue[dict]" = queue.Queue()
        self.cancel_event = threading.Event()

        self.logger = setup_logging(self.log_queue)
        self.logger.info("VideoSplitter application started.")

        self.settings_path = utils.get_settings_path()
        self.settings = self._load_settings()

        self.input_path_var = tk.StringVar(value=self.settings.get("input_path", ""))
        self.output_dir_var = tk.StringVar(
            value=self.settings.get(
                "output_dir", str(utils.get_default_output_dir())
            )
        )
        self.max_size_var = tk.StringVar(
            value=str(self.settings.get("max_size_gb", 1.5))
        )
        self.max_duration_var = tk.StringVar(
            value=str(self.settings.get("max_duration_minutes", 50))
        )

        self.status_var = tk.StringVar(value="待機中")
        self.progress_var = tk.DoubleVar(value=0.0)
        self.progress_label_var = tk.StringVar(value="進捗: 0 / 0")
        self.progress_total = 0

        self.worker_thread: Optional[threading.Thread] = None
        self.is_running = False

        self._build_ui()
        self.after(100, self._poll_log_queue)
        self.after(100, self._poll_event_queue)

        self.protocol("WM_DELETE_WINDOW", self._on_close)

    # UI construction -----------------------------------------------------
    def _build_ui(self) -> None:
        padding = {"padx": 10, "pady": 6}

        file_frame = ttk.LabelFrame(self, text="入力と出力")
        file_frame.pack(fill=tk.X, padx=12, pady=8)

        ttk.Label(file_frame, text="入力動画 (MP4):").grid(
            row=0, column=0, sticky=tk.W, **padding
        )
        self.input_entry = ttk.Entry(
            file_frame, textvariable=self.input_path_var, width=60
        )
        self.input_entry.grid(row=0, column=1, sticky=tk.EW, **padding)
        ttk.Button(
            file_frame, text="入力動画を選択", command=self._choose_input
        ).grid(row=0, column=2, sticky=tk.E, **padding)

        ttk.Label(file_frame, text="出力フォルダ:").grid(
            row=1, column=0, sticky=tk.W, **padding
        )
        self.output_entry = ttk.Entry(
            file_frame, textvariable=self.output_dir_var, width=60
        )
        self.output_entry.grid(row=1, column=1, sticky=tk.EW, **padding)
        ttk.Button(
            file_frame, text="参照...", command=self._choose_output_dir
        ).grid(row=1, column=2, sticky=tk.E, **padding)

        file_frame.columnconfigure(1, weight=1)

        options_frame = ttk.LabelFrame(self, text="分割条件")
        options_frame.pack(fill=tk.X, padx=12, pady=4)

        ttk.Label(options_frame, text="最大サイズ (GB):").grid(
            row=0, column=0, sticky=tk.W, **padding
        )
        ttk.Entry(
            options_frame, textvariable=self.max_size_var, width=10
        ).grid(row=0, column=1, sticky=tk.W, **padding)

        ttk.Label(options_frame, text="最大時間 (分):").grid(
            row=0, column=2, sticky=tk.W, **padding
        )
        ttk.Entry(
            options_frame, textvariable=self.max_duration_var, width=10
        ).grid(row=0, column=3, sticky=tk.W, **padding)

        controls_frame = ttk.Frame(self)
        controls_frame.pack(fill=tk.X, padx=12, pady=4)

        self.start_button = ttk.Button(
            controls_frame, text="開始", command=self._on_start
        )
        self.start_button.pack(side=tk.LEFT, padx=5)
        self.cancel_button = ttk.Button(
            controls_frame, text="キャンセル", command=self._on_cancel, state=tk.DISABLED
        )
        self.cancel_button.pack(side=tk.LEFT, padx=5)

        ttk.Label(controls_frame, textvariable=self.status_var).pack(
            side=tk.RIGHT, padx=5
        )

        progress_frame = ttk.Frame(self)
        progress_frame.pack(fill=tk.X, padx=12, pady=8)
        ttk.Progressbar(
            progress_frame,
            variable=self.progress_var,
            maximum=100.0,
            mode="determinate",
        ).pack(fill=tk.X, padx=4)
        ttk.Label(progress_frame, textvariable=self.progress_label_var).pack(
            anchor=tk.W, padx=4, pady=2
        )

        log_frame = ttk.LabelFrame(self, text="ログ")
        log_frame.pack(fill=tk.BOTH, expand=True, padx=12, pady=4)
        self.log_text = ScrolledText(log_frame, height=12, state=tk.DISABLED)
        self.log_text.pack(fill=tk.BOTH, expand=True, padx=6, pady=6)

    # Event handlers ------------------------------------------------------
    def _choose_input(self) -> None:
        file_path = filedialog.askopenfilename(
            title="MP4ファイルを選択",
            filetypes=[("MP4 Files", "*.mp4")],
        )
        if file_path:
            self.input_path_var.set(file_path)

    def _choose_output_dir(self) -> None:
        directory = filedialog.askdirectory(
            title="出力フォルダを選択",
            initialdir=self.output_dir_var.get() or str(utils.get_default_output_dir()),
        )
        if directory:
            self.output_dir_var.set(directory)

    def _on_start(self) -> None:
        if self.is_running:
            return

        try:
            input_path = Path(self.input_path_var.get()).expanduser().resolve()
        except Exception:
            messagebox.showerror("エラー", "入力ファイルのパスが不正です。")
            return

        if not input_path.exists() or input_path.suffix.lower() != ".mp4":
            messagebox.showerror("エラー", "有効なMP4ファイルを選択してください。")
            return

        try:
            output_dir = Path(self.output_dir_var.get()).expanduser().resolve()
        except Exception:
            messagebox.showerror("エラー", "出力フォルダのパスが不正です。")
            return

        try:
            max_size = float(self.max_size_var.get())
            max_duration = float(self.max_duration_var.get())
        except ValueError:
            messagebox.showerror("エラー", "最大サイズと最大時間には数値を入力してください。")
            return

        if max_size <= 0 or max_duration <= 0:
            messagebox.showerror("エラー", "最大サイズと最大時間は0より大きい値を指定してください。")
            return

        utils.ensure_directory(output_dir)

        existing_parts = list(output_dir.glob("part_*.mp4"))
        if existing_parts:
            if not messagebox.askyesno(
                "確認",
                f"出力フォルダに既存のpart_*.mp4が{len(existing_parts)}件存在します。上書きしますか？",
            ):
                return
            for part in existing_parts:
                try:
                    part.unlink()
                except OSError:
                    pass

        self.status_var.set("処理中...")
        self.progress_label_var.set("進捗: 0 / 0")
        self.progress_var.set(0.0)
        self.progress_total = 0
        self._set_running(True)
        self.cancel_event.clear()

        self._save_settings(
            input_path=str(input_path),
            output_dir=str(output_dir),
            max_size_gb=max_size,
            max_duration_minutes=max_duration,
        )

        self.worker_thread = threading.Thread(
            target=self._run_processing,
            args=(input_path, output_dir, max_size, max_duration),
            daemon=True,
        )
        self.worker_thread.start()

    def _on_cancel(self) -> None:
        if not self.is_running:
            return
        if not self.cancel_event.is_set():
            self.logger.info("Cancellation requested by user.")
            self.cancel_event.set()
            self.status_var.set("キャンセル中...")
        self.cancel_button.config(state=tk.DISABLED)

    def _on_close(self) -> None:
        if self.is_running and not messagebox.askyesno(
            "終了確認", "処理をキャンセルしてアプリケーションを終了しますか？"
        ):
            return
        self.cancel_event.set()
        self.destroy()

    # Background processing ----------------------------------------------
    def _run_processing(
        self, input_path: Path, output_dir: Path, max_size: float, max_duration: float
    ) -> None:
        try:
            ffmpeg_path, ffprobe_path = get_ffmpeg_paths()
            self.logger.info("Using ffmpeg at %s", ffmpeg_path)
            self.logger.info("Using ffprobe at %s", ffprobe_path)

            metadata = probe(input_path)
            duration = metadata.get("duration", 0.0)
            size_bytes = metadata.get("size_bytes", 0)
            bitrate = metadata.get("bit_rate", 0)
            self.logger.info(
                "Input video duration=%.2f秒 size=%s bitrate=%s bps",
                duration,
                utils.human_readable_size(size_bytes),
                bitrate,
            )

            plan = make_plan(duration, size_bytes, max_size, max_duration)
            parts = int(plan["parts"])
            segment_time = plan["segment_time"]
            self.logger.info(
                "Split plan: %d part(s), segment %.2f 秒.", parts, segment_time
            )

            self.event_queue.put(
                {"type": "status", "message": f"分割実行中... 全{parts}パート想定"}
            )

            part_files = copy_split(
                input_path,
                output_dir,
                segment_time,
                ffmpeg_path,
                self.logger,
                cancel_event=self.cancel_event,
            )

            total_parts = len(part_files)
            self.event_queue.put({"type": "progress_reset", "total": total_parts})
            self.event_queue.put(
                {"type": "status", "message": "サイズチェック & 再エンコード処理中..."}
            )

            def progress_callback(current: int, total: int) -> None:
                self.event_queue.put(
                    {"type": "progress", "current": current, "total": total}
                )

            final_files = reencode_oversize(
                part_files,
                max_size,
                ffmpeg_path,
                self.logger,
                cancel_event=self.cancel_event,
                progress_callback=progress_callback,
            )

            if self.cancel_event.is_set():
                self.event_queue.put({"type": "cancelled"})
            else:
                self.event_queue.put(
                    {
                        "type": "completed",
                        "files": [str(path) for path in final_files],
                        "output_dir": str(output_dir),
                    }
                )
        except utils.OperationCancelled:
            self.event_queue.put({"type": "cancelled"})
        except Exception as exc:
            self.logger.exception("An error occurred during processing: %s", exc)
            self.event_queue.put({"type": "error", "message": str(exc)})
        finally:
            self.event_queue.put({"type": "done"})

    # Queue polling -------------------------------------------------------
    def _poll_log_queue(self) -> None:
        while True:
            try:
                message = self.log_queue.get_nowait()
            except queue.Empty:
                break
            self._append_log(message)
        self.after(150, self._poll_log_queue)

    def _poll_event_queue(self) -> None:
        while True:
            try:
                event = self.event_queue.get_nowait()
            except queue.Empty:
                break
            self._handle_event(event)
        self.after(150, self._poll_event_queue)

    def _handle_event(self, event: dict) -> None:
        event_type = event.get("type")
        if event_type == "status":
            self.status_var.set(event.get("message", ""))
        elif event_type == "progress_reset":
            total = event.get("total", 0)
            self.progress_total = total
            self.progress_var.set(0.0)
            self.progress_label_var.set(f"進捗: 0 / {total}")
        elif event_type == "progress":
            current = event.get("current", 0)
            total = event.get("total", self.progress_total or 1)
            total = max(total, 1)
            percent = min(100.0, max(0.0, (current / total) * 100.0))
            self.progress_var.set(percent)
            self.progress_label_var.set(f"進捗: {current} / {total}")
        elif event_type == "completed":
            self.status_var.set("完了")
            self.progress_var.set(100.0)
            self.progress_label_var.set(
                f"進捗: {self.progress_total} / {self.progress_total}"
            )
            self._show_completion_dialog(event["files"], event["output_dir"])
        elif event_type == "error":
            self.status_var.set("エラー")
            messagebox.showerror("エラー発生", event.get("message", "不明なエラーが発生しました。"))
        elif event_type == "cancelled":
            self.status_var.set("キャンセル済み")
            messagebox.showinfo("キャンセル", "処理をキャンセルしました。")
        elif event_type == "done":
            self._set_running(False)

    # Helpers -------------------------------------------------------------
    def _append_log(self, message: str) -> None:
        self.log_text.configure(state=tk.NORMAL)
        self.log_text.insert(tk.END, message + "\n")
        self.log_text.see(tk.END)
        self.log_text.configure(state=tk.DISABLED)

    def _set_running(self, running: bool) -> None:
        self.is_running = running
        state = tk.DISABLED if running else tk.NORMAL
        for widget in (self.input_entry, self.output_entry):
            widget.configure(state=state)
        self.start_button.configure(state=tk.DISABLED if running else tk.NORMAL)
        self.cancel_button.configure(state=tk.NORMAL if running else tk.DISABLED)

    def _save_settings(
        self,
        *,
        input_path: str,
        output_dir: str,
        max_size_gb: float,
        max_duration_minutes: float,
    ) -> None:
        data = {
            "input_path": input_path,
            "output_dir": output_dir,
            "max_size_gb": max_size_gb,
            "max_duration_minutes": max_duration_minutes,
        }
        utils.save_json(self.settings_path, data)
        self.settings = data

    def _load_settings(self) -> dict:
        defaults = {
            "input_path": "",
            "output_dir": str(utils.get_default_output_dir()),
            "max_size_gb": 1.5,
            "max_duration_minutes": 50,
        }
        data = defaults.copy()
        data.update(utils.load_json(self.settings_path))
        if not Path(data["output_dir"]).is_absolute():
            data["output_dir"] = str(
                (utils.get_runtime_dir() / data["output_dir"]).resolve()
            )
        if not Path(self.settings_path).exists():
            utils.save_json(self.settings_path, data)
        return data

    def _show_completion_dialog(self, files: List[str], output_dir: str) -> None:
        dialog = tk.Toplevel(self)
        dialog.title("分割完了")
        dialog.transient(self)
        dialog.resizable(False, False)

        ttk.Label(
            dialog,
            text=f"{len(files)} 個のパートを生成しました。",
        ).pack(padx=16, pady=(16, 8))

        listbox = tk.Listbox(dialog, height=min(10, len(files)), width=60)
        for path in files:
            listbox.insert(tk.END, Path(path).name)
        listbox.pack(padx=16, pady=4)

        button_frame = ttk.Frame(dialog)
        button_frame.pack(fill=tk.X, padx=16, pady=(8, 16))

        ttk.Button(
            button_frame,
            text="フォルダを開く",
            command=lambda: self._open_folder(Path(output_dir)),
        ).pack(side=tk.LEFT, padx=4)
        ttk.Button(button_frame, text="閉じる", command=dialog.destroy).pack(
            side=tk.RIGHT, padx=4
        )

        dialog.grab_set()
        self.wait_window(dialog)

    def _open_folder(self, folder: Path) -> None:
        if not folder.exists():
            messagebox.showerror("エラー", f"フォルダが見つかりません: {folder}")
            return
        try:
            if sys.platform.startswith("win"):
                subprocess.Popen(["explorer", str(folder)])
            elif sys.platform == "darwin":
                subprocess.Popen(["open", str(folder)])
            else:
                subprocess.Popen(["xdg-open", str(folder)])
        except Exception as exc:
            self.logger.error("フォルダを開けませんでした: %s", exc)
            messagebox.showerror("エラー", f"フォルダを開けませんでした: {exc}")


def main() -> None:
    app = VideoSplitterApp()
    app.mainloop()


if __name__ == "__main__":
    main()
