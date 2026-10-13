"""Verify shipped package sizes, hashes and absence of test entry points against release notes."""
from pathlib import Path
import hashlib,re,zipfile
r=Path(__file__).resolve().parents[1]
notes=(r/'docs/RELEASES.md').read_text('utf-8').split('\n# ',1)[0]
for folder,name in [('Windows','AI-Image-Editing-Tools.exe'),('Android','AI-Image-Editing-Tools.apk')]:
 data=(r/'releases'/folder/name).read_bytes();assert f'| {folder}/{name} | {len(data)} bytes | `{hashlib.sha256(data).hexdigest()}` |' in notes,'Release hash/size mismatch: '+name
exe=(r/'releases/Windows/AI-Image-Editing-Tools.exe').read_bytes()
for text in ['SelfTest','ScrambleEngineTests','CoverScrambleTests','--parameter-vectors','--video-clean-test','--ui-test','--cover-test','--scramble-vectors']:
 assert text.encode() not in exe and text.encode('utf-16le') not in exe,text
with zipfile.ZipFile(r/'releases/Android/AI-Image-Editing-Tools.apk') as z:
 for name in z.namelist():
  if name.endswith('.dex'):
   b=z.read(name);assert b'Lcom/guangyingruoshui/imagetools/AndroidTests;' not in b and b'Lcom/guangyingruoshui/imagetools/AiIntegrationTests;' not in b
print('PASS: release-note hashes/sizes match; production test entries absent')
