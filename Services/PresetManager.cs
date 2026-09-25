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
    public class PresetManager
    {
        private readonly string _presetsFilePath;

        public static readonly List<CleansingPreset> BuiltInPresets = new()
        {
            new CleansingPreset
            {
                Id = "builtin-lines",
                Name = "纯文本/换行清洗",
                Pattern = @"(?m)^\s*([^\r\n]+?)\s*$",
                CaptureGroupIndex = 1,
                IsBuiltIn = true,
                Description = "按行匹配所有非空纯文本行"
            },
            new CleansingPreset
            {
                Id = "builtin-ebook",
                Name = "电子书标记清洗",
                Pattern = @"^[▪•\-\*\s]+([ぁ-んァ-ヶー\u4e00-\u9fa5a-zA-Z0-9]+)\s*$",
                CaptureGroupIndex = 1,
                IsBuiltIn = true,
                Description = "自动剔除行首电子书特殊符号、章节标记并提取单词"
            },
            new CleansingPreset
            {
                Id = "builtin-japanese",
                Name = "纯日文/汉字提取",
                Pattern = @"([ぁ-んァ-ヶー\u4e00-\u9fa5]+)",
                CaptureGroupIndex = 1,
                IsBuiltIn = true,
                Description = "忽略所有西文标点与排版符号，精准抽取日文假名及汉字词块"
            },
            new CleansingPreset
            {
                Id = "builtin-exclude-noise",
                Name = "排除注释与数字行",
                Pattern = @"^\s*(#|//|\d+|http[s]?:)",
                CaptureGroupIndex = 0,
                IsBuiltIn = true,
                IsInverseFilter = true,
                MatchWholeLine = true,
                Description = "反向过滤+匹配整行：命中注释、数字序号或链接时直接将整行删掉"
            },
            new CleansingPreset
            {
                Id = "builtin-strip-brackets",
                Name = "剔除括号与注音",
                Pattern = @"[（\(［\[\{【].*?[）\)］\]\}】]",
                CaptureGroupIndex = 0,
                IsBuiltIn = true,
                IsInverseFilter = true,
                MatchWholeLine = false,
                Description = "反向过滤：匹配各种括号与注音假名并删除，保留该行剩余干净词汇"
            }
        };

        public ObservableCollection<CleansingPreset> Presets { get; } = new();

        public PresetManager()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NihongoVocab");

            if (!Directory.Exists(appDataDir))
            {
                Directory.CreateDirectory(appDataDir);
            }

            _presetsFilePath = Path.Combine(appDataDir, "presets.json");
            LoadPresets();
        }

        public void LoadPresets()
        {
            Presets.Clear();
            foreach (var bp in BuiltInPresets)
            {
                Presets.Add(bp);
            }

            if (File.Exists(_presetsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_presetsFilePath);
                    var customPresets = JsonSerializer.Deserialize<List<CleansingPreset>>(json);
                    if (customPresets != null)
                    {
                        foreach (var cp in customPresets.Where(p => !p.IsBuiltIn))
                        {
                            Presets.Add(cp);
                        }
                    }
                }
                catch
                {
                    // 若文件损坏，忽略并使用默认
                }
            }
        }

        public void SaveCustomPresets()
        {
            var customOnly = Presets.Where(p => !p.IsBuiltIn).ToList();
            string json = JsonSerializer.Serialize(customOnly, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_presetsFilePath, json);
        }

        public void AddPreset(CleansingPreset preset)
        {
            preset.IsBuiltIn = false;
            Presets.Add(preset);
            SaveCustomPresets();
        }

        public void RemovePreset(CleansingPreset preset)
        {
            if (preset.IsBuiltIn) return;
            Presets.Remove(preset);
            SaveCustomPresets();
        }

        public void UpdatePreset(CleansingPreset preset)
        {
            if (preset.IsBuiltIn) return;
            SaveCustomPresets();
        }

        /// <summary>
        /// 执行正则清洗流水线：按预设执行正向/反向、片段/整行匹配清洗、首尾去空、自动去重
        /// </summary>
        public List<string> Cleanse(string sourceText, CleansingPreset preset)
        {
            return Cleanse(sourceText, preset, preset?.IsInverseFilter ?? false, preset?.MatchWholeLine ?? false);
        }

        /// <summary>
        /// 执行正则清洗流水线：支持显式指定是否反向过滤（删除匹配）及是否匹配整行
        /// </summary>
        public List<string> Cleanse(string sourceText, CleansingPreset preset, bool isInverseFilter)
        {
            return Cleanse(sourceText, preset, isInverseFilter, preset?.MatchWholeLine ?? false);
        }

        /// <summary>
        /// 执行正则清洗流水线：支持显式指定是否反向过滤及是否匹配整行
        /// </summary>
        public List<string> Cleanse(string sourceText, CleansingPreset preset, bool isInverseFilter, bool matchWholeLine)
        {
            if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(preset?.Pattern))
            {
                return new List<string>();
            }

            var results = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var regex = new Regex(preset.Pattern, RegexOptions.Multiline);

                if (isInverseFilter)
                {
                    // ===== 反向过滤 =====
                    var lines = sourceText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    foreach (var rawLine in lines)
                    {
                        if (string.IsNullOrWhiteSpace(rawLine)) continue;

                        if (matchWholeLine)
                        {
                            // 反向过滤 + 匹配整行：命中任意字符，整行直接删掉
                            if (regex.IsMatch(rawLine))
                            {
                                continue;
                            }

                            string line = rawLine.Trim();
                            if (!string.IsNullOrEmpty(line) && !seen.Contains(line))
                            {
                                seen.Add(line);
                                results.Add(line);
                            }
                        }
                        else
                        {
                            // 反向过滤 (未勾选匹配整行)：单纯匹配字符并删除，保留该行剩余文本
                            string cleanedLine = regex.Replace(rawLine, string.Empty).Trim();
                            if (!string.IsNullOrEmpty(cleanedLine) && !seen.Contains(cleanedLine))
                            {
                                seen.Add(cleanedLine);
                                results.Add(cleanedLine);
                            }
                        }
                    }
                }
                else
                {
                    // ===== 正向过滤 =====
                    if (matchWholeLine)
                    {
                        // 正向过滤 + 匹配整行：只要行内命中任意字符，就将整行输出
                        var lines = sourceText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                        foreach (var rawLine in lines)
                        {
                            if (string.IsNullOrWhiteSpace(rawLine)) continue;

                            if (regex.IsMatch(rawLine))
                            {
                                string line = rawLine.Trim();
                                if (!string.IsNullOrEmpty(line) && !seen.Contains(line))
                                {
                                    seen.Add(line);
                                    results.Add(line);
                                }
                            }
                        }
                    }
                    else
                    {
                        // 正向过滤 (未勾选匹配整行)：仅提取匹配到的片段或捕获组
                        var matches = regex.Matches(sourceText);
                        foreach (Match match in matches)
                        {
                            if (match.Success)
                            {
                                string extracted = string.Empty;
                                if (preset.CaptureGroupIndex > 0 && match.Groups.Count > preset.CaptureGroupIndex)
                                {
                                    extracted = match.Groups[preset.CaptureGroupIndex].Value;
                                }
                                else
                                {
                                    extracted = match.Value;
                                }

                                string clean = extracted.Trim();
                                if (!string.IsNullOrEmpty(clean) && !seen.Contains(clean))
                                {
                                    seen.Add(clean);
                                    results.Add(clean);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // 正则表达式有误时优雅返回空结果
            }

            return results;
        }
    }
}
