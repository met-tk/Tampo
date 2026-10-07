# -*- coding: utf-8 -*-
import os
from PIL import Image

ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_ICON = os.path.join(ROOT_DIR, "Assets", "AppIcon_Rounded_1000.png")
RES_DIR = os.path.join(ROOT_DIR, "tampo_android", "android", "app", "src", "main", "res")

scales = {
    "mipmap-mdpi": 48,
    "mipmap-hdpi": 72,
    "mipmap-xhdpi": 96,
    "mipmap-xxhdpi": 144,
    "mipmap-xxxhdpi": 192,
}

if os.path.exists(SRC_ICON):
    im = Image.open(SRC_ICON).convert("RGBA")
    for folder, size in scales.items():
        out_dir = os.path.join(RES_DIR, folder)
        os.makedirs(out_dir, exist_ok=True)
        out_path = os.path.join(out_dir, "ic_launcher.png")
        resized = im.resize((size, size), Image.Resampling.LANCZOS)
        resized.save(out_path)
        print(f"Generated {out_path} ({size}x{size})")
    print("ALL_ANDROID_ICONS_SYNCED")
else:
    print(f"Error: {SRC_ICON} not found")
