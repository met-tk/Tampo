# -*- coding: utf-8 -*-
"""
基于根目录原版 ta.png 生成高质量圆角图标与 ICO 资源。
直接使用 ta.png 的完整像素内容，施加 4x 超采样平滑抗锯齿圆角蒙版，绝不重新绘制笔画！
"""
import os
from PIL import Image, ImageDraw

ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TA_PNG_PATH = os.path.join(ROOT_DIR, "ta.png")
ASSETS_DIR = os.path.join(ROOT_DIR, "Assets")
INSTALLER_DIR = os.path.join(ROOT_DIR, "Installer")

def generate():
    if not os.path.exists(TA_PNG_PATH):
        print(f"Error: {TA_PNG_PATH} does not exist!")
        return

    # 1. 打开原版 ta.png
    base_img = Image.open(TA_PNG_PATH).convert("RGBA")
    w, h = base_img.size

    # 2. 超采样生成平滑圆角蒙版 (4x 超采样消除边缘锯齿)
    scale = 4
    ss_w, ss_h = w * scale, h * scale
    mask_hi = Image.new("L", (ss_w, ss_h), 0)
    draw_hi = ImageDraw.Draw(mask_hi)

    radius = int(ss_w * 0.22)  # Windows 11 Fluent 图标标准 22% 圆角
    draw_hi.rounded_rectangle([0, 0, ss_w - 1, ss_h - 1], radius=radius, fill=255)

    # 降采样得到抗锯齿的高质量 Alpha 蒙版
    mask = mask_hi.resize((w, h), Image.Resampling.LANCZOS)

    # 3. 将蒙版应用于原图
    rounded_icon = base_img.copy()
    rounded_icon.putalpha(mask)

    # 保存预览
    rounded_icon.save(os.path.join(ASSETS_DIR, "AppIcon_Rounded_1000.png"))
    print("Generated 1000x1000 rounded icon from original ta.png successfully.")

    # 4. 生成包含全部标准 Windows 尺寸的 ICO 文件
    SIZES = [(256, 256), (128, 128), (96, 96), (64, 64), (48, 48), (32, 32), (24, 24), (16, 16)]
    for target_dir in [ASSETS_DIR, INSTALLER_DIR]:
        ico_path = os.path.join(target_dir, "AppIcon.ico")
        rounded_icon.save(ico_path, format="ICO", sizes=SIZES)
        print(f"Saved multi-size ICO -> {ico_path}")

    # 5. 更新 Windows App SDK / WinUI 3 所需的所有 PNG 资产
    assets_specs = {
        "Square44x44Logo.scale-200.png": (88, 88),
        "Square44x44Logo.targetsize-48.png": (48, 48),
        "Square44x44Logo.targetsize-24_altform-unplated.png": (24, 24),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": (48, 48),
        "Square150x150Logo.scale-200.png": (300, 300),
        "StoreLogo.png": (50, 50),
        "LockScreenLogo.scale-200.png": (48, 48),
        "Wide310x150Logo.scale-200.png": (620, 300),
        "SplashScreen.scale-200.png": (1240, 600),
    }

    for fname, (tw, th) in assets_specs.items():
        dest_path = os.path.join(ASSETS_DIR, fname)
        out = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
        if tw == th:
            scaled = rounded_icon.resize((tw, th), Image.Resampling.LANCZOS)
            out.paste(scaled, (0, 0), scaled)
        else:
            icon_dim = int(min(tw, th) * 0.75)
            scaled = rounded_icon.resize((icon_dim, icon_dim), Image.Resampling.LANCZOS)
            pos = ((tw - icon_dim) // 2, (th - icon_dim) // 2)
            out.paste(scaled, pos, scaled)
        out.save(dest_path)
        print(f"Generated asset: {fname} ({tw}x{th})")

    print("ALL_ICONS_AND_ASSETS_SUCCESSFULLY_GENERATED")

if __name__ == "__main__":
    generate()
