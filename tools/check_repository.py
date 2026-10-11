"""Check public repository paths, secrets, Markdown links and release assets."""
from pathlib import Path
import re
import zipfile
import json

ROOT = Path(__file__).resolve().parents[1]
SKIP = {'.git', 'build', '__pycache__', '.vs', '.idea', '.vscode'}
errors = []
files = []
for path in ROOT.rglob('*'):
    relative = path.relative_to(ROOT)
    if any(part in SKIP for part in relative.parts):
        continue
    if not relative.as_posix().isascii():
        errors.append('Non-English path: ' + str(relative))
    if path.is_dir() and not any(path.iterdir()):
        errors.append('Empty directory: ' + str(relative))
    if path.is_file():
        files.append(path)
        if path.stat().st_size == 0:
            errors.append('Zero-byte file: ' + str(relative))
        if path.suffix.lower() in {'.jks', '.keystore', '.pyc', '.log', '.tmp', '.idsig'} or path.name in {'password.txt', '.env', 'local.properties'}:
            errors.append('Private/generated file: ' + str(relative))
        if path.suffix.lower() == '.txt' and relative.as_posix() not in {'ai/requirements.txt', 'android/native/CMakeLists.txt'}:
            errors.append('Loose TXT file: ' + str(relative))
for path in files:
    if path.suffix == '.md':
        text = path.read_text(encoding='utf-8-sig')
        if re.search(r'\?{3,}', text):
            errors.append('Damaged text encoding: ' + str(path.relative_to(ROOT)))
        for link in re.findall(r'\[[^\]]*\]\(([^)]+)\)', text):
            if '://' in link or link.startswith('#'):
                continue
            target = link.split('#')[0]
            if target and not (path.parent / target).exists():
                errors.append('Broken local link: ' + str(path.relative_to(ROOT)) + ': ' + target)
expected = {'releases/Windows/AI-Image-Editing-Tools.exe', 'releases/Android/AI-Image-Editing-Tools.apk'}
actual = {p.relative_to(ROOT).as_posix() for p in files if p.suffix.lower() in {'.exe', '.apk'}}
if actual != expected:
    errors.append('Expected exactly the two named release packages')
apk = ROOT / 'releases/Android/AI-Image-Editing-Tools.apk'
if apk.exists():
    with zipfile.ZipFile(apk) as archive:
        if archive.testzip() is not None:
            errors.append('Corrupt APK ZIP entry')
        assets = {n for n in archive.namelist() if n.startswith('assets/') and not n.endswith('/')}
        if assets != {'assets/NOTICE.md', 'assets/tutorial.zh.json', 'assets/tutorial.en.json', 'assets/android-ai-models.json'}:
            errors.append('Unexpected release assets: ' + repr(assets))
        for language in ('zh', 'en'):
            name = f'assets/tutorial.{language}.json'
            if name in assets and archive.read(name) != (ROOT / name).read_bytes():
                errors.append('Outdated bundled tutorial: ' + name)
        if archive.read('assets/android-ai-models.json') != (ROOT / 'assets/android-ai-models.json').read_bytes():
            errors.append('Outdated Android pinned model manifest')
        for abi in ('arm64-v8a', 'x86_64'):
            for library in ('libaipt_diffusion.so', 'libonnxruntime.so', 'libonnxruntime4j_jni.so'):
                if 'lib/' + abi + '/' + library not in archive.namelist():
                    errors.append('Missing offline AI native library: ' + abi + '/' + library)
        for abi in ('armeabi-v7a', 'x86'):
            if 'lib/' + abi + '/libaipt_compat.so' not in archive.namelist():
                errors.append('Missing ordinary-tool installation ABI: ' + abi)
        notice = archive.read('assets/NOTICE.md').decode('utf-8')
        if 'MIT License' not in notice or 'BSD 2-Clause License' not in notice or 'Apache License' not in notice:
            errors.append('Missing embedded licenses')
for required in ['LICENSE', 'README.md', 'README.en.md', 'THIRD_PARTY_NOTICES.md', '.gitignore', '.gitattributes']:
    if not (ROOT / required).is_file():
        errors.append('Missing repository file: ' + required)
chapter_order = ['dual', 'background', 'scramble', 'disguise', 'gif', 'clean', 'redact', 'tags', 'qr', 'settings']
for language, readme in [('zh', 'README.md'), ('en', 'README.en.md')]:
    guide = json.loads((ROOT / f'assets/tutorial.{language}.json').read_text(encoding='utf-8'))
    if [chapter['id'] for chapter in guide['chapters']] != chapter_order:
        errors.append('Tutorial order differs from navigation: ' + language)
    document = (ROOT / readme).read_text(encoding='utf-8')
    for chapter in guide['chapters']:
        for content in [chapter['intro'], chapter['output']] + chapter['steps'] + chapter['options']:
            if not content or content not in document:
                errors.append('README/tutorial content mismatch: ' + language + '/' + chapter['id'])
if errors:
    raise SystemExit('\n'.join(errors))
print('PASS: ASCII paths, no empty directories/files, release packages, embedded licenses, Markdown links and private-file checks')
print('Public files:', len(files), 'Total bytes:', sum(p.stat().st_size for p in files))
