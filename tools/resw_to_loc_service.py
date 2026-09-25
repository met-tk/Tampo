import os
import json
import xml.etree.ElementTree as ET

def load_resw(path):
    d = {}
    if not os.path.exists(path):
        return d
    tree = ET.parse(path)
    root = tree.getroot()
    for item in root.findall('data'):
        k = item.attrib.get('name')
        val_elem = item.find('value')
        v = val_elem.text if val_elem is not None else ""
        if k:
            d[k] = v
    return d

zh_dict = load_resw(r'Strings\zh-CN\Resources.resw')
en_dict = load_resw(r'Strings\en-US\Resources.resw')
ja_dict = load_resw(r'Strings\ja-JP\Resources.resw')

all_keys = sorted(list(set(list(zh_dict.keys()) + list(en_dict.keys()) + list(ja_dict.keys()))))
print(f"Total unique keys across resw files: {len(all_keys)}")

def build_switch(dict_data):
    lines = []
    for k in all_keys:
        v = dict_data.get(k, k)
        val_quoted = json.dumps(v, ensure_ascii=False)
        lines.append(f'                    "{k}" => {val_quoted},')
    return '\n'.join(lines)

ja_code = build_switch(ja_dict)
en_code = build_switch(en_dict)
zh_code = build_switch(zh_dict)

cs_path = r'Services\LocalizationService.cs'
with open(cs_path, 'r', encoding='utf-8') as f:
    lines = f.readlines()

prefix_lines = []
for line in lines:
    if "private static string GetFallbackString" in line:
        break
    prefix_lines.append(line)

new_code = "".join(prefix_lines) + f"""        private static string GetFallbackString(string key, string lang)
        {{
            return lang switch
            {{
                "ja-JP" => key switch
                {{
{ja_code}
                    _ => key
                }},
                "en-US" => key switch
                {{
{en_code}
                    _ => key
                }},
                _ => key switch
                {{
{zh_code}
                    _ => key
                }}
            }};
        }}
    }}
}}
"""

with open(cs_path, 'w', encoding='utf-8') as f:
    f.write(new_code)

print("Updated Services/LocalizationService.cs with all resw keys successfully!")
