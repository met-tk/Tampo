import xml.etree.ElementTree as ET
import os

def load_resw(path):
    tree = ET.parse(path)
    root = tree.getroot()
    data = {}
    for d in root.findall('data'):
        name = d.attrib.get('name')
        val = d.find('value')
        if val is not None and val.text:
            data[name] = val.text.strip()
    return data

zh = load_resw(r'Strings\zh-CN\Resources.resw')
en = load_resw(r'Strings\en-US\Resources.resw')
ja = load_resw(r'Strings\ja-JP\Resources.resw')

print(f'zh-CN keys: {len(zh)}')
print(f'en-US keys: {len(en)}')
print(f'ja-JP keys: {len(ja)}')

missing_in_en = set(zh.keys()) - set(en.keys())
missing_in_ja = set(zh.keys()) - set(ja.keys())

print('Missing in en:', missing_in_en)
print('Missing in ja:', missing_in_ja)

for k, v in sorted(zh.items()):
    en_v = en.get(k, 'MISSING')
    ja_v = ja.get(k, 'MISSING')
    print(f'{k:26} | ZH: {v[:20]:20} | EN: {en_v[:20]:20} | JA: {ja_v[:20]:20}')
