import os
import re

views_dir = 'Views'
files_to_check = ['MainWindow.xaml', 'MainWindow.xaml.cs']

for root, _, files in os.walk(views_dir):
    for f in files:
        if f.endswith('.xaml') or f.endswith('.xaml.cs'):
            files_to_check.append(os.path.join(root, f))

chinese_re = re.compile(r'[\u4e00-\u9fa5]+')

results = {}

for fpath in files_to_check:
    if not os.path.exists(fpath): continue
    with open(fpath, 'r', encoding='utf-8', errors='ignore') as f:
        lines = f.readlines()
    
    file_matches = []
    for idx, line in enumerate(lines, 1):
        # 忽略纯注释
        stripped = line.strip()
        if stripped.startswith('//') or stripped.startswith('<!--') or stripped.startswith('*') or stripped.startswith('/*'):
            continue
        # 如果是 xaml 里的注释
        if '<!--' in line and '-->' in line:
            # 简单剥离
            line_no_comment = re.sub(r'<!--.*?-->', '', line)
        else:
            line_no_comment = line
        
        # 查找包含中文的字串
        m = chinese_re.search(line_no_comment)
        if m:
            # 检查是否是已本地化：如 loc.GetString("Key", "中文")，这是允许的（有 fallback）
            # 或者 x:Uid="SomeKey" Text="中文" 也是设计时预览，但若没有 x:Uid 则需要检查
            file_matches.append((idx, stripped))
    if file_matches:
        results[fpath] = file_matches

for fpath, matches in results.items():
    print(f"=== {fpath} ({len(matches)} lines with Chinese) ===")
    for idx, line in matches[:15]:
        print(f"  L{idx:4d}: {line[:100]}")
    if len(matches) > 15:
        print(f"  ... and {len(matches) - 15} more lines.")
