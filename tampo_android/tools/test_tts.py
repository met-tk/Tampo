import urllib.request
import urllib.parse

candidates = [
    ('有道日语TTS', 'https://dict.youdao.com/dictvoice?audio={w}&le=jap'),
    ('谷歌日语TTS', 'https://translate.google.com/translate_tts?ie=UTF-8&tl=ja&client=tw-ob&q={w}'),
    ('百度日语TTS', 'https://fanyi.baidu.com/gettts?lan=jp&text={w}&spd=3&source=web'),
    ('沪江小D', 'https://dict.hjenglish.com/services/calltts.ashx?type=jp&word={w}'),
]

for name, tmpl in candidates:
    url = tmpl.format(w=urllib.parse.quote('空威張り'))
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'})
    try:
        with urllib.request.urlopen(req, timeout=5) as r:
            data = r.read()
            print(f'{name}: len={len(data)}, type={r.headers.get("Content-Type")}')
    except Exception as e:
        print(f'{name}: err={e}')
