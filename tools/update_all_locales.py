import os
import re
import json

# 1. 扫描所有 .cs 文件中的 loc.GetString("Key", "Fallback")
cs_files = []
for root, _, files in os.walk('.'):
    if 'bin' in root or 'obj' in root or '.git' in root: continue
    for f in files:
        if f.endswith('.cs'):
            cs_files.append(os.path.join(root, f))

pattern = re.compile(r'loc\.GetString\(\s*"([^"]+)"\s*,\s*"([^"]*)"\s*\)')

code_keys = {}
for path in cs_files:
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        text = f.read()
    for m in pattern.finditer(text):
        k, fb = m.group(1), m.group(2)
        if k not in code_keys:
            code_keys[k] = fb

print(f"Total keys found in C# code: {len(code_keys)}")

# 2. 读取当前的 LocalizationService.cs 字典
cs_loc_path = 'Services/LocalizationService.cs'
with open(cs_loc_path, 'r', encoding='utf-8') as f:
    loc_text = f.read()

def extract_dict(lang_marker):
    start = loc_text.find(lang_marker)
    if start == -1: return {}
    end = loc_text.find('};', start)
    sub = loc_text[start:end]
    d = {}
    for match in re.finditer(r'"([^"]+)"\s*=>\s*"([^"]*)"', sub):
        d[match.group(1)] = match.group(2)
    return d

ja_dict = extract_dict('"ja-JP" =>')
en_dict = extract_dict('"en-US" =>')
zh_dict = extract_dict('_ => key switch')

print(f"Current dict count: ja={len(ja_dict)}, en={len(en_dict)}, zh={len(zh_dict)}")

# 3. 找出尚未在字典中的 key
missing_keys = set(code_keys.keys()) - set(ja_dict.keys())
print(f"Keys in code but missing in dict: {len(missing_keys)}")
for k in sorted(missing_keys):
    print(f"  {k} => '{code_keys[k]}'")
