"""Prepare pinned ORT and reuse the shipped native libraries for Java/package CI.
Native source changes require a separate native rebuild using android/native/build_native.py.
"""
from pathlib import Path
import hashlib,json,urllib.request,zipfile,io
r=Path(__file__).resolve().parents[1];out=r/'build/android-ci';out.mkdir(parents=True,exist_ok=True)
lock=json.loads((r/'android/native/onnxruntime.lock.json').read_text());data=urllib.request.urlopen(lock['url'],timeout=120).read()
assert len(data)==lock['bytes'] and hashlib.sha256(data).hexdigest()==lock['sha256']
ort=out/'ort';ort.mkdir(exist_ok=True)
with zipfile.ZipFile(io.BytesIO(data)) as z:
 for name,integrity in lock['files'].items():
  b=z.read(name);assert len(b)==integrity['bytes'] and hashlib.sha256(b).hexdigest()==integrity['sha256'];p=ort/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
native=out/'native'
with zipfile.ZipFile(r/'releases/Android/AI-Image-Editing-Tools.apk') as z:
 for abi in ['arm64-v8a','x86_64','armeabi-v7a','x86']:
  name='libaipt_diffusion.so' if abi in ['arm64-v8a','x86_64'] else 'libaipt_compat.so';p=native/abi/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(z.read('lib/'+abi+'/'+name))
print('Pinned ORT verified; native release libraries prepared for Java/package CI')
