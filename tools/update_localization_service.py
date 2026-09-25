# -*- coding: utf-8 -*-
import os
import re
from sync_locales import entries

import json

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
    content = f.read()

new_fallback_body = f"""        private static string GetFallbackString(string key, string lang)
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
        }}"""

pattern = r'        private static string GetFallbackString\(string key, string lang\)\s*\{[\s\S]*?\n        \}'
new_content = re.sub(pattern, new_fallback_body, content)

with open(cs_path, 'w', encoding='utf-8') as f:
    f.write(new_content)

print(f"Successfully updated {cs_path} with {len(entries)} fallback entries per language!")
