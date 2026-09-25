using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NihongoVocab.Models;

namespace NihongoVocab.Services
{
    public class ExportPresetManager
    {
        private static ExportPresetManager? _instance;
        public static ExportPresetManager Instance => _instance ??= new ExportPresetManager();

        private readonly string _presetsFilePath;

        public static readonly List<ExportPreset> BuiltInPresets = new()
        {
            new ExportPreset
            {
                Id = "builtin-export-words-only",
                Name = "纯单词换行 (Line-by-Line)",
                Pattern = @"^(.*)$",
                Replacement = "$1",
                IsBuiltIn = true,
                Description = "导出单词字面，每行一个单词"
            },
            new ExportPreset
            {
                Id = "builtin-export-numbered",
                Name = "序号清单列表 (1. 单词)",
                Pattern = @"^(.*)$",
                Replacement = "{index}. $1",
                IsBuiltIn = true,
                Description = "按行导出为带自增序号的列表清单"
            },
            new ExportPreset
            {
                Id = "builtin-export-bullet",
                Name = "Markdown 列表清单 (- 单词)",
                Pattern = @"^(.*)$",
                Replacement = "- $1",
                IsBuiltIn = true,
                Description = "按行导出为 Markdown 无序列表项"
            },
            new ExportPreset
            {
                Id = "builtin-export-bracket-wrap",
                Name = "符号包裹格式 (【单词】)",
                Pattern = @"^(.*)$",
                Replacement = "【$1】",
                IsBuiltIn = true,
                Description = "使用日文方头括号或其他符号包裹单词"
            },
            new ExportPreset
            {
                Id = "builtin-export-quoted",
                Name = "引号/CSV 兼容格式 (\"单词\")",
                Pattern = @"^(.*)$",
                Replacement = "\"$1\"",
                IsBuiltIn = true,
                Description = "为每个单词包裹双引号，便于数据迁移"
            },
            new ExportPreset
            {
                Id = "builtin-export-split-bracket",
                Name = "括号读音拆分 (若含括号)",
                Pattern = @"^(.*?)\s*[\(（](.*?)[\)）]$",
                Replacement = "$1\t$2",
                IsBuiltIn = true,
                Description = "自动提取并拆分形如「食べる(たべる)」为两列"
            },
            new ExportPreset
            {
                Id = "builtin-export-anki-card",
                Name = "Anki 基础制卡格式",
                Pattern = @"^(.*)$",
                Replacement = "$1\t",
                IsBuiltIn = true,
                Description = "正面单词加制表符，留空背面，适合批量制卡"
            }
        };

        public ObservableCollection<ExportPreset> Presets { get; } = new();

        public ExportPresetManager()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NihongoVocab");

            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }

            _presetsFilePath = Path.Combine(appDataDir, "export_presets.json");
            LoadPresets();
        }

        public void LoadPresets()
        {
            Presets.Clear();

            // 1. 加载内置预设
            foreach (var bp in BuiltInPresets)
            {
                Presets.Add(bp);
            }

            // 2. 加载用户自定义预设
            if (File.Exists(_presetsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_presetsFilePath);
                    var customPresets = JsonSerializer.Deserialize<List<ExportPreset>>(json);
                    if (customPresets != null)
                    {
                        foreach (var cp in customPresets)
                        {
                            cp.IsBuiltIn = false;
                            Presets.Add(cp);
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashLogger.LogException(ex, "ExportPresetManager.LoadPresets");
                }
            }
        }

        public void SaveCustomPresets()
        {
            try
            {
                var customPresets = Presets.Where(p => !p.IsBuiltIn).ToList();
                string json = JsonSerializer.Serialize(customPresets, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_presetsFilePath, json);
            }
            catch (Exception ex)
            {
                CrashLogger.LogException(ex, "ExportPresetManager.SaveCustomPresets");
            }
        }

        public void AddPreset(ExportPreset preset)
        {
            preset.IsBuiltIn = false;
            Presets.Add(preset);
            SaveCustomPresets();
        }

        public void RemovePreset(ExportPreset preset)
        {
            if (preset.IsBuiltIn) return;
            Presets.Remove(preset);
            SaveCustomPresets();
        }

        /// <summary>
        /// 将单个单词对象转换为标准输入源行（单词\t假名\t释义）
        /// </summary>
        public static string FormatWordToSourceLine(Word word)
        {
            return word.Text ?? string.Empty;
        }

        /// <summary>
        /// 使用给定的 Pattern 和 Replacement 正则替换处理单行
        /// </summary>
        public static string FormatLine(string sourceLine, string pattern, string replacement, int index = 1)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return sourceLine;
            }

            try
            {
                // 处理替换串中的转义符如 \t, \n 以及常用占位符 {index}, {word}
                string unescapedReplacement = replacement
                    .Replace(@"\t", "\t")
                    .Replace(@"\n", "\n")
                    .Replace(@"\r", "\r")
                    .Replace("{index}", index.ToString())
                    .Replace("{word}", sourceLine);

                if (Regex.IsMatch(sourceLine, pattern))
                {
                    return Regex.Replace(sourceLine, pattern, unescapedReplacement);
                }
                else
                {
                    return sourceLine;
                }
            }
            catch
            {
                return sourceLine;
            }
        }
    }
}
