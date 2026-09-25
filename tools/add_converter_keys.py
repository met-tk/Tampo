import os
import xml.etree.ElementTree as ET
from xml.dom import minidom

converter_entries = [
    ("RetentionSolid", "记忆牢固", "Solid Memory", "記憶定着"),
    ("RetentionGood", "状态良好", "Good State", "良好な状態"),
    ("RetentionCritical", "临界遗忘，建议复习", "Near Forgetting, Review Suggested", "忘却寸前、復習推奨"),
    ("RetentionUrgent", "急需复习强化", "Urgent Review Needed", "至急復習が必要"),
    ("RetentionPending", "待学习", "To Learn", "未学習"),
    ("StabilityUnreviewed", "预计维持：尚未复习", "Stability: Not Reviewed", "予測維持：未復習"),
    ("StabilityHoursFormat", "预计维持：约 {0:F0} 小时", "Stability: ~{0:F0} hours", "予測維持：約 {0:F0} 時間"),
    ("StabilityDaysFormat", "预计维持：约 {0:F1} 天", "Stability: ~{0:F1} days", "予測維持：約 {0:F1} 日"),
    ("DifficultyInitial", "难度：初始新词", "Difficulty: New Word", "難易度：新規単語"),
    ("DifficultyEasyFormat", "难度：{0:F1} (较易)", "Difficulty: {0:F1} (Easy)", "難易度：{0:F1} (易しい)"),
    ("DifficultyMediumFormat", "难度：{0:F1} (适中)", "Difficulty: {0:F1} (Medium)", "難易度：{0:F1} (普通)"),
    ("DifficultyHardFormat", "难度：{0:F1} (较难)", "Difficulty: {0:F1} (Hard)", "難易度：{0:F1} (難しい)"),
    ("WordsSuffixFormat", "{0} 词", "{0} words", "{0} 語"),
    ("StateUnknown", "未知", "Unknown", "不明"),
]

def append_to_resw(file_path, lang_idx):
    if not os.path.exists(file_path):
        return
    tree = ET.parse(file_path)
    root = tree.getroot()
    existing_keys = {elem.attrib.get('name') for elem in root.findall('data')}
    
    added = 0
    for key, zh, en, ja in converter_entries:
        if key not in existing_keys:
            val = (key, zh, en, ja)[lang_idx]
            data = ET.SubElement(root, 'data', name=key)
            data.set('{http://www.w3.org/XML/1998/namespace}space', 'preserve')
            ET.SubElement(data, 'value').text = val
            added += 1

    rough_str = ET.tostring(root, encoding='utf-8')
    parsed = minidom.parseString(rough_str)
    # remove extra blank lines created by pretty printing
    pretty = parsed.toprettyxml(indent="  ", encoding='utf-8')
    with open(file_path, 'wb') as f:
        f.write(pretty)
    print(f"Appended {added} keys to {file_path}")

append_to_resw(r'Strings\zh-CN\Resources.resw', 1)
append_to_resw(r'Strings\en-US\Resources.resw', 2)
append_to_resw(r'Strings\ja-JP\Resources.resw', 3)
