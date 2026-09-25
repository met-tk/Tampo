using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NihongoVocab.Models;

namespace NihongoVocab.Services
{
    public class WebExportService
    {
        public async Task<string> ExportLibraryToHtmlAsync(IEnumerable<Word> words)
        {
            string exportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NihongoVocab", "exports");

            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            string filePath = Path.Combine(exportDir, "vocab_library.html");

            var wordListDto = new List<object>();
            foreach (var w in words)
            {
                wordListDto.Add(new
                {
                    id = w.Id,
                    text = w.Text,
                    createdAt = w.CreatedAt.ToString("yyyy-MM-dd"),
                    year = w.CreatedAt.Year,
                    month = w.CreatedAt.ToString("yyyy-MM"),
                    state = w.State switch
                    {
                        0 => "新词",
                        1 => "学习中",
                        2 => "复习中",
                        3 => "已掌握",
                        _ => "未学习"
                    },
                    reps = w.Reps,
                    stability = w.Stability.ToString("F1"),
                    retention = (w.CurrentRetrievability * 100).ToString("F0") + "%"
                });
            }

            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = false
            };
            string wordsJson = JsonSerializer.Serialize(wordListDto, jsonOptions);

            string htmlContent = $@"<!DOCTYPE html>
<html lang=""zh-CN"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Tampo - Web 离线词单全景视图</title>
    <style>
        :root {{
            --bg-color: #0f172a;
            --surface-color: #1e293b;
            --border-color: #334155;
            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --accent-color: #38bdf8;
            --accent-glow: rgba(56, 189, 248, 0.2);
            --card-hover: #273549;
        }}
        * {{
            box-sizing: border-box;
            margin: 0;
            padding: 0;
        }}
        body {{
            background-color: var(--bg-color);
            color: var(--text-primary);
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", ""Yu Gothic UI"", ""Hiragino Sans"", Roboto, sans-serif;
            padding: 24px;
            line-height: 1.5;
        }}
        .container {{
            max-width: 1300px;
            margin: 0 auto;
        }}
        header {{
            display: flex;
            justify-content: space-between;
            align-items: center;
            padding-bottom: 20px;
            border-bottom: 1px solid var(--border-color);
            margin-bottom: 24px;
        }}
        .header-title {{
            display: flex;
            align-items: center;
            gap: 12px;
        }}
        .badge {{
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            padding: 4px 10px;
            border-radius: 6px;
            font-size: 13px;
            color: var(--text-secondary);
        }}
        .controls {{
            display: flex;
            flex-wrap: wrap;
            gap: 12px;
            margin-bottom: 24px;
            background: var(--surface-color);
            padding: 16px;
            border-radius: 10px;
            border: 1px solid var(--border-color);
        }}
        .search-box {{
            flex: 2;
            min-width: 240px;
        }}
        .filter-box {{
            flex: 1;
            min-width: 180px;
        }}
        input, select {{
            width: 100%;
            background: var(--bg-color);
            border: 1px solid var(--border-color);
            color: var(--text-primary);
            padding: 10px 14px;
            border-radius: 6px;
            font-size: 14px;
            outline: none;
            transition: border-color 0.2s;
        }}
        input:focus, select:focus {{
            border-color: var(--accent-color);
        }}
        .grid {{
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
            gap: 16px;
        }}
        .card {{
            background: var(--surface-color);
            border: 1px solid var(--border-color);
            border-radius: 8px;
            padding: 16px;
            display: flex;
            flex-direction: column;
            justify-content: center;
            align-items: center;
            transition: transform 0.2s, border-color 0.2s, background-color 0.2s;
            position: relative;
        }}
        .card:hover {{
            transform: translateY(-2px);
            border-color: var(--accent-color);
            background-color: var(--card-hover);
        }}
        .word-text {{
            font-size: 20px;
            font-weight: 600;
            margin-bottom: 8px;
            letter-spacing: 0.5px;
        }}
        .word-meta {{
            font-size: 11px;
            color: var(--text-secondary);
            display: flex;
            gap: 8px;
        }}
        .count-indicator {{
            margin-top: 8px;
            font-size: 13px;
            color: var(--accent-color);
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <header>
            <div class=""header-title"">
                <h1>Tampo · 离线全景词单</h1>
                <span class=""badge"">Zero-Definition 纯单词模式</span>
            </div>
            <div class=""badge"" id=""totalCountBadge"">总计: 0 词</div>
        </header>

        <div class=""controls"">
            <div class=""search-box"">
                <input type=""text"" id=""searchInput"" placeholder=""实时搜索单词..."" autocomplete=""off"">
            </div>
            <div class=""filter-box"">
                <select id=""timeFilter"">
                    <option value=""all"">全部时间周期</option>
                </select>
            </div>
            <div class=""filter-box"">
                <select id=""stateFilter"">
                    <option value=""all"">全部学习状态</option>
                    <option value=""新词"">新词</option>
                    <option value=""学习中"">学习中</option>
                    <option value=""复习中"">复习中</option>
                    <option value=""已掌握"">已掌握</option>
                </select>
            </div>
        </div>

        <div class=""count-indicator"" id=""filteredCount""></div>
        <div class=""grid"" id=""wordGrid""></div>
    </div>

    <script>
        const words = {wordsJson};

        const searchInput = document.getElementById('searchInput');
        const timeFilter = document.getElementById('timeFilter');
        const stateFilter = document.getElementById('stateFilter');
        const wordGrid = document.getElementById('wordGrid');
        const filteredCount = document.getElementById('filteredCount');
        const totalCountBadge = document.getElementById('totalCountBadge');

        totalCountBadge.textContent = '总词汇量: ' + words.length + ' 词';

        // 提取月份周期
        const months = Array.from(new Set(words.map(w => w.month))).filter(Boolean).sort().reverse();
        months.forEach(m => {{
            const opt = document.createElement('option');
            opt.value = m;
            opt.textContent = m + ' 月度导入';
            timeFilter.appendChild(opt);
        }});

        function renderWords() {{
            const query = searchInput.value.trim().toLowerCase();
            const selectedMonth = timeFilter.value;
            const selectedState = stateFilter.value;

            const filtered = words.filter(w => {{
                if (query && !w.text.toLowerCase().includes(query)) return false;
                if (selectedMonth !== 'all' && w.month !== selectedMonth) return false;
                if (selectedState !== 'all' && w.state !== selectedState) return false;
                return true;
            }});

            filteredCount.textContent = '当前显示：' + filtered.length + ' 词';
            wordGrid.innerHTML = '';

            filtered.forEach(w => {{
                const card = document.createElement('div');
                card.className = 'card';
                card.innerHTML = `
                    <div class=""word-text"">${{w.text}}</div>
                    <div class=""word-meta"">
                        <span>${{w.state}}</span>
                        <span>${{w.createdAt}}</span>
                        <span>留存: ${{w.retention}}</span>
                    </div>
                `;
                wordGrid.appendChild(card);
            }});
        }}

        searchInput.addEventListener('input', renderWords);
        timeFilter.addEventListener('change', renderWords);
        stateFilter.addEventListener('change', renderWords);

        renderWords();
    </script>
</body>
</html>";

            await File.WriteAllTextAsync(filePath, htmlContent, Encoding.UTF8);
            return filePath;
        }

        public async Task OpenInBrowserAsync(IEnumerable<Word> words)
        {
            string htmlPath = await ExportLibraryToHtmlAsync(words);
            Process.Start(new ProcessStartInfo(htmlPath)
            {
                UseShellExecute = true
            });
        }
    }
}
