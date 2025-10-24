# -*- mode: python ; coding: utf-8 -*-

from pathlib import Path

project_root = Path(__file__).resolve().parents[1]

block_cipher = None

a = Analysis(
    ["src/app.py"],
    pathex=[str(project_root)],
    binaries=[
        (str(project_root / "third_party/ffmpeg/win-x64/ffmpeg.exe"), "."),
        (str(project_root / "third_party/ffmpeg/win-x64/ffprobe.exe"), "."),
    ],
    datas=[
        (str(project_root / "settings.json"), "."),
        (str(project_root / "third_party/ffmpeg/win-x64/LICENSE.txt"), "licenses"),
    ],
    hiddenimports=[],
    hookspath=[],
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)
pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)
exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name="VideoSplitter",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    icon=str(project_root / "assets/icon.ico"),
)

