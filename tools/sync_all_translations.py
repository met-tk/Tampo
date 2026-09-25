# -*- coding: utf-8 -*-
import os
import re
import xml.etree.ElementTree as ET
from xml.dom import minidom

# 现有的 239 个词条直接从 LocalizationService.cs 提取，保证已有翻译一个不丢
cs_path = 'Services/LocalizationService.cs'
with open(cs_path, 'r', encoding='utf-8') as f:
    cs_content = f.read()

def extract_existing(marker):
    start = cs_content.find(marker)
    if start == -1: return {}
    end = cs_content.find('};', start)
    sub = cs_content[start:end]
    d = {}
    for match in re.finditer(r'"([^"]+)"\s*=>\s*"([^"]*)"', sub):
        d[match.group(1)] = match.group(2)
    return d

ja_all = extract_existing('"ja-JP" =>')
en_all = extract_existing('"en-US" =>')
zh_all = extract_existing('_ => key switch')

# 新增补齐的 102 个词条
new_entries = {
    "ActivityCalendar_ButtonToday": ("今天", "Today", "今日"),
    "ActivityCalendar_TooltipNextPeriod": ("切换至下一周期", "Next Period", "次の期間へ移動"),
    "ActivityCalendar_TooltipPrevPeriod": ("切换至上一周期", "Previous Period", "前の期間へ移動"),
    "ButtonAnotherSession": ("再来一组", "Another Session", "もう1セッション"),
    "ButtonConfirmCreate": ("确认创建", "Confirm Create", "作成を確定"),
    "ButtonConfirmDelete": ("确认删除", "Confirm Delete", "削除を確定"),
    "ButtonConfirmDissolve": ("确认解散", "Confirm Dissolve", "解散を確定"),
    "ButtonConfirmPack": ("确认打包", "Confirm Pack", "パッケージを確定"),
    "ButtonDeleteCompletely": ("彻底删除", "Delete Permanently", "完全に削除"),
    "ButtonManageList": ("管理词单", "Manage List", "単語帳を管理"),
    "ButtonManagePresets": ("管理清洗预设", "Manage Presets", "クレンジングプリセット管理"),
    "ButtonMerge": ("合并", "Merge", "結合"),
    "ButtonSaveNewPreset": ("保存为新预设", "Save New Preset", "新規プリセットとして保存"),
    "CaptureGroupHeader": ("捕获组索引", "Capture Group Index", "キャプチャグループ番号"),
    "ConfigRulesTitle": ("自定义清洗规则配置", "Custom Cleaning Rules", "カスタムクレンジング設定"),
    "DecayForecastTitle": ("记忆状态预测（衰减曲线）", "Memory Decay Forecast", "記憶状態の減衰予測"),
    "DefaultListNamePrefix": ("自选词单", "Custom List", "カスタム単語帳"),
    "DefaultSliceListNamePrefix": ("切片词单", "Slice List", "スライス単語帳"),
    "DetailImportCountFormat": ("新收录 {0} 词", "Imported {0} words", "新規収録 {0} 語"),
    "DetailNoImportWords": ("当日无新词收录", "No imported words on this day", "この日の新規収録なし"),
    "DetailNoReviewLogs": ("当日无打卡复习记录", "No review records on this day", "この日の復習記録なし"),
    "DetailReviewCountFormat": ("复习打卡 {0} 词", "Reviewed {0} words", "復習完了 {0} 語"),
    "DetailTabImport": ("新收录词", "Imported Words", "新規収録"),
    "DetailTabReview": ("复习打卡", "Reviewed Words", "復習打刻"),
    "DialogMsgDeleteListCompletely": ("确定要彻底删除词单「{0}」及其包含的所有单词吗？\\n此操作不可撤销，关联的单词和复习记录将彻底从数据库中移除。", "Are you sure you want to permanently delete list '{0}' and all its words?\\nThis action cannot be undone. Associated words and review logs will be removed.", "単語帳「{0}」および含まれるすべての単語を完全に削除しますか？\\nこの操作は元に戻せません。関連する単語と復習ログは完全に削除されます。"),
    "DialogMsgDeletePresetFormat": ("确定要删除自定义预设「{0}」吗？", "Are you sure you want to delete custom preset '{0}'?", "カスタムプリセット「{0}」を削除しますか？"),
    "DialogMsgDeleteWordsFormat": ("确定要永久删除所选的 {0} 个单词吗？\\n此操作将同时删除相关复习记录且不可撤销。", "Are you sure you want to permanently delete the selected {0} words?\\nThis will also remove associated review logs and cannot be undone.", "選択した {0} 語を完全に削除しますか？\\n関連する復習ログも同時に削除され、元に戻すことはできません。"),
    "DialogMsgDissolveList": ("确定要解散词单「{0}」吗？\\n解散后单词本身不会丢失，将重新恢复为未归档状态，并依原来导入的时间进行排序。", "Are you sure you want to dissolve list '{0}'?\\nWords will not be deleted; they will return to unarchived status and be sorted by import date.", "単語帳「{0}」を解散しますか？\\n解散しても単語は失われず、未分類状態に戻り、元のインポート日時に従って配置されます。"),
    "DialogMsgNoOtherListsToMerge": ("当前没有其他词单可供合并。", "No other word lists available to merge into.", "結合可能な他の単語帳がありません。"),
    "DialogMsgPackSelectionFormat": ("即将把当前选中的 {0} 个单词打包为新词单：", "Packing selected {0} words into a new word list:", "選択された {0} 語を新しい単語帳にパッケージします："),
    "DialogMsgPackSliceFormat": ("即将把时间切片「{0}」下的所有词汇打包为新词单：", "Packing all words under time slice '{0}' into a new word list:", "タイムスライス「{0}」のすべての単語を新しい単語帳にパッケージします："),
    "DialogMsgPleaseSelectWordsToPack": ("请先在右侧列表中勾选需要打包为新词单的单词！", "Please select words in the list to pack into a new word list first!", "新しい単語帳にパッケージする単語を右側のリストで選択してください！"),
    "DialogMsgSelectTargetListFormat": ("请选择将「{0}」合并入哪个目标词单：", "Please select the target list to merge '{0}' into:", "「{0}」をどの対象単語帳に結合するか選択してください："),
    "DialogTitleCreateCustomList": ("创建自选专属词单", "Create Custom Word List", "カスタム専用単語帳を作成"),
    "DialogTitleCreateTip": ("创建提示", "Create Tip", "作成ヒント"),
    "DialogTitleDayDetail": ("当日活动详情", "Day Activity Details", "当日の学習詳細"),
    "DialogTitleDeleteListCompletely": ("彻底删除词单", "Delete List Permanently", "単語帳を完全削除"),
    "DialogTitleDeletePreset": ("删除清洗预设", "Delete Preset", "クレンジングプリセット削除"),
    "DialogTitleDeleteWords": ("删除所选单词", "Delete Selected Words", "選択した単語を削除"),
    "DialogTitleDissolveList": ("确认解散词单", "Confirm Dissolve List", "単語帳の解散を確認"),
    "DialogTitleMergeList": ("合并词单", "Merge Word Lists", "単語帳の結合"),
    "DialogTitleMergeTip": ("合并提示", "Merge Tip", "結合のヒント"),
    "DialogTitlePackSlice": ("打包当前切片为新词单", "Pack Slice to New List", "現在のスライスを単語帳にパッケージ"),
    "DialogTitleRenameList": ("重命名词单", "Rename Word List", "単語帳の名前を変更"),
    "DialogTitleWordDetail": ("单词记忆与复习详情", "Word Memory & Review Details", "単語の記憶・復習詳細"),
    "EngineLabel": ("算法引擎: FSRS v4.5 (Free Spaced Repetition Scheduler)", "Algorithm Engine: FSRS v4.5", "アルゴリズム：FSRS v4.5"),
    "Import_OriginalTextPlaceholder": ("输入或粘贴文本...", "Enter or paste text here...", "テキストを入力または貼り付け..."),
    "InstantVerifyTitle": ("单行即时提取验证", "Instant Line Test", "1行リアルタイム抽出テスト"),
    "LabelExtractedCount": ("提取总数:", "Extracted Count:", "抽出総数："),
    "LabelForgotten": ("遗忘", "Forgotten", "忘却"),
    "LabelPendingStudy": ("待学", "Pending", "学習待ち"),
    "LabelPreset": ("文本清洗预设:", "Cleaning Preset:", "クレンジングプリセット："),
    "LabelRemembered": ("记得", "Remembered", "記憶"),
    "LabelStateFilter": ("状态筛选:", "State Filter:", "状態フィルタ："),
    "LabelStudyScope": ("学习范围:", "Study Scope:", "学習範囲："),
    "LabelTimeSpan": ("时间跨度:", "Time Span:", "期間："),
    "LabelTodayStudy": ("今日学习:", "Today's Study:", "本日の学習："),
    "LabelTotalFound": ("共计查得:", "Total Found:", "合計検索結果："),
    "Language": ("语言", "Language", "言語"),
    "MemoryAnalytics_SearchPlaceholder": ("搜索历史单词...", "Search history words...", "履歴単語を検索..."),
    "MemoryAnalytics_TooltipBrowseStateWords": ("在全部词库中查看该状态下的全部单词", "View all words under this state in vocabulary", "単語帳ですべての単語を表示"),
    "MenuDeleteListCompletely": ("彻底删除词单及全部单词", "Permanently Delete List & Words", "単語帳とすべての単語を完全に削除"),
    "MenuDissolveList": ("解散词单（词汇恢复未归档）", "Dissolve List (Return to Unarchived)", "単語帳を解散（未分類へ戻す）"),
    "MenuExportList": ("在浏览器中查看", "View in Browser", "ブラウザで表示"),
    "MenuSetStateMastered": ("标记为：已掌握", "Mark as Mastered", "習得済みに設定"),
    "MenuSetStateNew": ("标记为：未学习", "Mark as New", "未学習に設定"),
    "MetricDifficultyLabel": ("FSRS 记忆难度", "FSRS Difficulty", "FSRS 難易度"),
    "MetricNotEvaluated": ("尚未评测", "Not Evaluated", "未評価"),
    "MetricReviewStatsLabel": ("复习统计", "Review Stats", "復習統計"),
    "MetricStabilityLabel": ("FSRS 稳定性 (半衰期)", "FSRS Stability (Half-life)", "FSRS 安定性 (半減期)"),
    "MsgStudyFinishedTip": ("目前没有需要学习或复习的单词。", "There are currently no words due for study or review.", "現在、学習または復習が必要な単語はありません。"),
    "MsgStudyFinishedTitle": ("今日复习已全部完成！", "Today's Review Completed!", "本日の復習はすべて完了しました！"),
    "NoReviewLogsYet": ("该单词尚未建立复习打卡日志", "No review logs for this word yet", "この単語には復習打刻ログがまだありません"),
    "PlaceholderNewListName": ("请输入新词单名称...", "Enter new list name...", "新しい単語帳名を入力..."),
    "PlaceholderRenameList": ("输入新的词单名称...", "Enter new list name...", "新しい単語帳名を入力..."),
    "PromptCandidates": ("清洗并提取出的候选词（鼠标悬浮高亮在原文中的位置，点击可移除单个）：", "Extracted candidate words (hover to highlight in text, click to remove):", "抽出された候補単語（ホバーで本文内の位置を強調、クリックで除外）："),
    "PromptRawText": ("在此粘贴或输入需要提取的日语原文（支持批量长文输入）：", "Paste or type raw Japanese text here (supports batch long text):", "日本語の原文を貼り付けまたは入力（長文の一括入力に対応）："),
    "RatingForgotten": ("遗忘", "Forgotten", "忘却"),
    "RatingRemembered": ("记得", "Remembered", "記憶"),
    "RecentStudy_SearchPlaceholder": ("在查得词汇中检索...", "Search in results...", "検索結果から絞り込み..."),
    "RecentStudy_SelectAll": ("全选查得词汇", "Select All Found Words", "すべての検索結果を選択"),
    "ReviewStatsFormat": ("已复习 {0} 次 / 遗忘 {1} 次", "Reviewed {0} times / Lapsed {1} times", "復習 {0} 回 / 忘却 {1} 回"),
    "ReviewTimelineFormat": ("学习与复习历史记录时间轴 ({0} 次)", "Study & Review Timeline ({0} times)", "学習・復習履歴タイムライン ({0} 回)"),
    "SavedRulesTip": ("点击选择可带入右侧表单进行测试或编辑", "Click to load into form for testing or editing", "選択して右側のフォームでテスト・編集"),
    "SavedRulesTitle": ("已保存规则库", "Saved Rules", "保存済みルール一覧"),
    "SectionLogs": ("运行日志", "Application Logs", "実行ログ"),
    "StatusTierAll": ("全部打卡", "All Checked-in", "すべての打刻"),
    "StatusTierForget": ("遗忘 (Again)", "Forgotten (Again)", "忘却 (Again)"),
    "StatusTierRemember": ("记得 (Good)", "Remembered (Good)", "記憶 (Good)"),
    "StudyStart_AllDueWordsPlaceholder": ("全部到期词汇", "All Due Words", "すべての期日到達単語"),
    "TestLinePlaceholder": ("输入单行测试文本...", "Enter single line test text...", "テスト用の1行テキストを入力..."),
    "TimeSpanDay": ("日 (最近24h)", "Day (Past 24h)", "日 (最近24時間)"),
    "TimeSpanMonth": ("月 (最近30天)", "Month (Past 30d)", "月 (最近30日)"),
    "TimeSpanQuarter": ("季度 (最近90天)", "Quarter (Past 90d)", "四半期 (最近90日)"),
    "TimeSpanWeek": ("周 (最近7天)", "Week (Past 7d)", "週 (最近7日)"),
    "TipMiddleClickCopy": ("提示：中键点击单词可直接复制到剪贴板", "Tip: Middle-click any word to copy to clipboard", "ヒント：中クリックで単語をクリップボードにコピー"),
    "ValidationNoInput": ("提取结果：(无输入)", "Result: (No input)", "抽出結果：(入力なし)"),
    "ValidationNoMatch": ("未匹配到任何结果。", "No matches found.", "一致する結果はありませんでした。"),
    "ValidationRegexError": ("正则语法错误：", "Regex syntax error: ", "正規表現構文エラー："),
    "ValidationSuccessFormat": ("提取成功：「{0}」 (捕获组: {1})", "Extracted: '{0}' (Group: {1})", "抽出成功：「{0}」 (グループ: {1})"),
    "ViewModeGrid": ("网格", "Grid", "グリッド"),
    "ViewModeList": ("列表", "List", "リスト"),
}

for k, (zh, en, ja) in new_entries.items():
    zh_all[k] = zh
    en_all[k] = en
    ja_all[k] = ja

all_keys = sorted(list(set(zh_all.keys()) | set(en_all.keys()) | set(ja_all.keys())))
print(f"Total merged keys: {len(all_keys)}")

# 1. 重写 Services/LocalizationService.cs
def gen_csharp_switch(d):
    lines = []
    for k in all_keys:
        v = d.get(k, k).replace('"', '\\"').replace('\n', '\\n')
        lines.append(f'                    "{k}" => "{v}",')
    return '\n'.join(lines)

ja_code = gen_csharp_switch(ja_all)
en_code = gen_csharp_switch(en_all)
zh_code = gen_csharp_switch(zh_all)

prefix_pos = cs_content.find("private static string GetFallbackString")
prefix = cs_content[:prefix_pos]

new_cs = prefix + f"""private static string GetFallbackString(string key, string lang)
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
    f.write(new_cs)
print("Updated Services/LocalizationService.cs successfully!")

# 2. 生成 Strings/*/Resources.resw
def write_resw(filepath, lang_dict):
    root = ET.Element('root')
    
    # 增加 resheader 元数据
    for hname, htype in [('resmimetype', 'text/microsoft-resx'), ('version', '2.0'), 
                         ('reader', 'System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089'),
                         ('writer', 'System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089')]:
        h = ET.SubElement(root, 'resheader', {'name': hname})
        v = ET.SubElement(h, 'value')
        v.text = htype

    for k in all_keys:
        val = lang_dict.get(k, k)
        data = ET.SubElement(root, 'data', {'name': k, 'xml:space': 'preserve'})
        v = ET.SubElement(data, 'value')
        v.text = val

    xml_str = minidom.parseString(ET.tostring(root, encoding='utf-8')).toprettyxml(indent="  ", encoding="utf-8")
    os.makedirs(os.path.dirname(filepath), exist_ok=True)
    with open(filepath, 'wb') as f:
        f.write(xml_str)
    print(f"Wrote {filepath}")

write_resw('Strings/zh-CN/Resources.resw', zh_all)
write_resw('Strings/en-US/Resources.resw', en_all)
write_resw('Strings/ja-JP/Resources.resw', ja_all)
print("All locales generated perfectly!")
