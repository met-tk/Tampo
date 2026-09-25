import re

with open('Services/LocalizationService.cs', 'r', encoding='utf-8') as f:
    text = f.read()

def extract_dict(lang_marker):
    start = text.find(lang_marker)
    if start == -1: return {}
    end = text.find('};', start)
    sub = text[start:end]
    d = {}
    for match in re.finditer(r'"([^"]+)"\s*=>\s*"([^"]*)"', sub):
        d[match.group(1)] = match.group(2)
    return d

ja = extract_dict('"ja-JP" =>')
en = extract_dict('"en-US" =>')
zh = extract_dict('_ => key switch')

print(f'ja: {len(ja)}, en: {len(en)}, zh: {len(zh)}')
all_keys = set(ja.keys()) | set(en.keys()) | set(zh.keys())
print(f'total unique keys: {len(all_keys)}')
missing_in_ja = all_keys - set(ja.keys())
missing_in_en = all_keys - set(en.keys())
missing_in_zh = all_keys - set(zh.keys())
print(f'missing in ja: {missing_in_ja}')
print(f'missing in en: {missing_in_en}')
print(f'missing in zh: {missing_in_zh}')
