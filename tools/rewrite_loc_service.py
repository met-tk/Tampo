# -*- coding: utf-8 -*-
import os
import json
from sync_locales import entries

def gen_switch(lang_idx):
    lines = []
    for item in entries:
        k = item[0]
        val_quoted = json.dumps(item[lang_idx], ensure_ascii=False)
        lines.append(f'                    "{k}" => {val_quoted},')
    return '\n'.join(lines)

ja_code = gen_switch(3)
en_code = gen_switch(2)
zh_code = gen_switch(1)

cs_path = os.path.join(os.path.dirname(__file__), '..', 'Services', 'LocalizationService.cs')
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

print("Rewrote LocalizationService.cs successfully!")
