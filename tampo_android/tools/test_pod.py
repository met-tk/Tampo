import urllib.request
import urllib.parse

words = ['空威張り', 'からいばり', '切実', 'エキス', 'のっぺり', 'テスト', '本', '食べる']
for w in words:
    url = f'https://assets.languagepod101.com/dictionary/japanese/audiomp3.php?kanji={urllib.parse.quote(w)}'
    req = urllib.request.Request(url, headers={'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'})
    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            data = resp.read()
            print(f'{w}: status={resp.status}, len={len(data)}, type={resp.headers.get("Content-Type")}')
            if len(data) < 2000:
                print(f'   data preview: {data[:200]}')
    except Exception as e:
        print(f'{w}: err={e}')
