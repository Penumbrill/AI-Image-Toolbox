"""Local-only image captioning and ControlNet inference; no WebUI or server."""
from pathlib import Path
import ctypes
import gc
import hashlib
import importlib.metadata
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import urllib.request
import zipfile
from concurrent.futures import ThreadPoolExecutor

LOCK = threading.Lock()
PYTHON_URL = 'https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip'
PYTHON_SHA = '009d6bf7e3b2ddca3d784fa09f90fe54336d5b60f0e0f305c37f400bf83cfd3b'
PIP_URL = 'https://files.pythonhosted.org/packages/44/3c/d717024885424591d5376220b5e836c2d5293ce2011523c9de23ff7bf068/pip-25.3-py3-none-any.whl'
PIP_SHA = '9655943313a94722b7774661c21049070f6bbb0a1516bf02f7c8d5d9201514cd'
VERSIONS = {'torch': '2.8.0', 'diffusers': '0.35.1', 'transformers': '4.57.1', 'accelerate': '1.11.0', 'safetensors': '0.6.2', 'Pillow': '12.0.0', 'huggingface-hub': '0.36.0', 'onnxruntime': '1.23.2', 'zxing-cpp': '2.3.0'}

class QrScanFailed(RuntimeError):
    """All generation candidates failed the ordinary, exact-content scan gate."""

def emit(event, **values):
    with LOCK:
        print(json.dumps(dict(event=event, **values), ensure_ascii=False), flush=True)

def report_error(error):
    emit('error', code='scan_failed' if isinstance(error, QrScanFailed) else 'failed', message=str(error))

def sha(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()

def inside(root, relative):
    root = Path(root).resolve()
    result = (root / relative).resolve()
    if not result.is_relative_to(root):
        raise ValueError('Invalid local path')
    return result

def atomic_json(path, value):
    path=Path(path)
    temporary=path.with_name(path.name+f'.{os.getpid()}.part')
    try:
        temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2),encoding='utf-8')
        os.replace(temporary,path)
    finally:
        temporary.unlink(missing_ok=True)

def model_fingerprint(path):
    """Include file identity and Windows ChangeTime, not merely a restored mtime."""
    path=Path(path).resolve();stat=path.stat()
    changed=stat.st_ctime_ns
    if sys.platform=='win32':
        import msvcrt
        class Basic(ctypes.Structure):
            _fields_=[('created',ctypes.c_longlong),('accessed',ctypes.c_longlong),('written',ctypes.c_longlong),('changed',ctypes.c_longlong),('attributes',ctypes.c_ulong)]
        with path.open('rb') as handle:
            basic=Basic()
            get=ctypes.windll.kernel32.GetFileInformationByHandleEx
            get.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_void_p,ctypes.c_ulong]
            if not get(msvcrt.get_osfhandle(handle.fileno()),0,ctypes.byref(basic),ctypes.sizeof(basic)):
                # Disable cache hits if the stronger identity cannot be obtained.
                changed=time.time_ns()
            else:changed=basic.changed
    return [str(path),stat.st_size,stat.st_mtime_ns,changed,stat.st_dev,stat.st_ino]

class ModelVerifier:
    def __init__(self,root,force=False,progress=None):
        self.path=inside(root,'model-hashes.json');self.force=force;self.entries={};self.checked={};self.progress=progress
        try:
            value=json.loads(self.path.read_text(encoding='utf-8'))
            if isinstance(value,dict) and isinstance(value.get('files'),dict):self.entries=value['files']
        except (OSError,ValueError,AttributeError):pass
    def verify(self,path,file,label):
        path=Path(path)
        def report(have,phase):
            if self.progress:self.progress(label,have,phase)
        try:
            before=model_fingerprint(path)
            if before[1]!=file['size']:report(0,'invalid');return False
            key=before[0];saved=self.entries.get(key,{})
            if not isinstance(saved,dict):saved={}
            session=self.checked.get(key,{})
            if session.get('fingerprint')==before:
                okay=session['sha256']==file['sha256'];report(before[1] if okay else 0,'verified' if okay else 'invalid');return okay
            if not self.force and saved.get('fingerprint')==before and isinstance(saved.get('sha256'),str):
                okay=saved['sha256']==file['sha256'];report(before[1] if okay else 0,'verified' if okay else 'invalid');return okay
            digest=hashlib.sha256();have=0;started=updated=time.monotonic()
            emit('verification',file=label,bytes=0,total=before[1],speed=0,phase='verifying')
            report(0,'verifying')
            with path.open('rb') as stream:
                for block in iter(lambda:stream.read(1024*1024),b''):
                    digest.update(block);have+=len(block);now=time.monotonic()
                    if now-updated>=.4:
                        emit('verification',file=label,bytes=have,total=before[1],speed=have/max(.001,now-started),phase='verifying');report(have,'verifying');updated=now
            if model_fingerprint(path)!=before:raise IOError('Model changed during verification / 模型在校验时发生变化')
            actual=digest.hexdigest();self.entries[key]={'fingerprint':before,'sha256':actual};self.checked[key]=self.entries[key]
            okay=actual==file['sha256']
            emit('verification',file=label,bytes=have,total=before[1],speed=have/max(.001,time.monotonic()-started),phase='verified' if okay else 'invalid',verified=okay)
            report(have if okay else 0,'verified' if okay else 'invalid')
            return okay
        except (FileNotFoundError,PermissionError,IsADirectoryError):report(0,'invalid');return False
    def save(self):atomic_json(self.path,{'version':1,'files':self.entries})

def package_candidates(package,roots):
    """Bounded layout lookup: explicit roots, known subdirectories and pinned HF snapshots."""
    found=[];seen=set()
    def add(path):
        try:
            path=Path(path).resolve();key=os.path.normcase(str(path))
            if key not in seen and path.is_dir():seen.add(key);found.append(path)
        except (OSError,ValueError):pass
    for root in roots[:64]:
        root=Path(root);target=package.get('source_target',package['target']);add(root/target);add(root/Path(target).name);add(root)
        repo=root/('models--'+package['repo'].replace('/','--'))
        for hub in (repo,root/'hub'/repo.name):
            add(hub/'snapshots'/package['revision'])
            try:
                for snapshot in sorted((hub/'snapshots').iterdir())[:16]:add(snapshot)
            except OSError:pass
        for models in (root/'diffusers',root/'models'/'diffusers'):
            try:
                for child in sorted(models.iterdir())[:48]:add(child)
            except OSError:pass
    return found[:192]

def package_path(root,target):
    """Read-only external package reference. The downloader never writes through it."""
    root=Path(root).resolve()
    try:
        value=json.loads(inside(root,'model-reuse.json').read_text(encoding='utf-8'))
        records=value.get('packages') if isinstance(value,dict) else None
        record=records.get(target) if isinstance(records,dict) else None
    except (OSError,ValueError,KeyError,AttributeError):record=None
    if record is not None:
        if not isinstance(record,dict):raise IOError('Invalid model reference; check models again / 模型引用记录无效，请重新检查模型')
        manifest=json.loads(inside(root,'engine/models.json').read_text(encoding='utf-8'))
        packages=manifest['packages']
        if target.startswith('configs/'):
            family=target.rsplit('/',1)[1];control=family.startswith('controlnet-');family=family.replace('controlnet-','',1)
            packages=[dict(p,target=target,files=[f for f in p['files'] if not f['local'].endswith(('.bin','.safetensors'))]) for p in packages if p['family']==family and p['target'].startswith('controlnet/' if control else 'base/')]
        expected=next((p['files'] for p in packages if p['target']==target),None)
        if not expected or not isinstance(record.get('path'),str) or record.get('files')!=expected:
            raise IOError('Invalid model reference; check models again / 模型引用记录无效，请重新检查模型')
        path=Path(record['path']).resolve();verifier=ModelVerifier(root)
        if not all(verifier.verify((inside(root,target) if optional_model_document(f) else path)/f['local'],f,target+'/'+f['local']) for f in expected):
            raise IOError('Referenced model changed; check models again / 引用的模型已变化，请重新检查模型')
        verifier.save();return path
    return inside(root,target)

def optional_model_document(file):
    name=Path(file['local'])
    return len(name.parts)==1 and (name.name.lower().startswith('readme') or name.name.lower() in ('license','license.txt','license.md','notice','notice.txt','.gitattributes'))

def mainland_region(request):
    if 'prefer_mirror' in request:
        return request['prefer_mirror'] is True
    if sys.platform == 'win32':
        try:
            geo = ctypes.windll.kernel32.GetUserGeoID(16)
            country = ctypes.create_unicode_buffer(8)
            if geo != -1 and ctypes.windll.kernel32.GetGeoInfoW(geo, 4, country, len(country), 0):
                return country.value.upper() == 'CN'
        except (AttributeError, OSError):
            pass
    return False

def endpoint_probe(url):
    started = time.monotonic()
    try:
        # Some model/CDN proxies reject tiny Range requests although ordinary
        # downloads work. Probe a small manifest URL with a normal GET and read
        # only one byte, then close the response.
        request = urllib.request.Request(url, headers={'User-Agent':'AI-Image-Toolbox/LocalAI', 'Accept-Encoding':'identity'})
        with urllib.request.urlopen(request, timeout=4) as response:
            if response.status not in (200, 206) or not response.read(1):
                raise IOError('Download endpoint unavailable')
        return {'reachable':True, 'latency_ms':round((time.monotonic()-started)*1000)}
    except Exception:
        return {'reachable':False, 'latency_ms':None}

def choose_sources(request, official, mirror, suffix, kind='models'):
    source = request.get('source', 'auto')
    if source not in ('auto','official','mirror'):
        raise ValueError('Invalid download source')
    if source != 'auto':
        return [official if source == 'official' else mirror]
    emit('progress', message='Checking download sources locally / 本地检测下载源可达性与延迟')
    with ThreadPoolExecutor(max_workers=2) as pool:
        jobs = {domain:pool.submit(endpoint_probe, domain+suffix) for domain in (official,mirror)}
        probes = {domain:job.result() for domain,job in jobs.items()}
    region = mainland_region(request)
    if region and probes[mirror]['reachable']:
        ordered = [mirror,official]
        reason = 'region'
    else:
        ordered = sorted((official,mirror), key=lambda domain:(not probes[domain]['reachable'],probes[domain]['latency_ms'] if probes[domain]['reachable'] else float('inf')))
        reason = 'latency' if any(p['reachable'] for p in probes.values()) else 'fallback'
    emit('network', kind=kind, source='mirror' if ordered[0]==mirror else 'official', reason=reason,
         endpoints=[dict(source='mirror' if domain==mirror else 'official', **probes[domain]) for domain in (official,mirror)])
    return ordered

def dependency_mirror(request):
    return choose_sources(request,'https://pypi.org','https://pypi.tuna.tsinghua.edu.cn','/simple/pip/','dependencies')[0].startswith('https://pypi.tuna.')

def download(url, target, expected, size=None, label=None, progress=None):
    target = Path(target)
    label = label or target.name
    def report(have, total, speed=0, phase='downloading', **extra):
        percent = min(99.9, have*100/total) if total and phase not in ('cached','verified') else (100 if phase in ('cached','verified') else None)
        emit('download', file=label, bytes=have, total=total, speed=max(0,speed), percent=percent, phase=phase, **extra)
        if progress:progress(label,have,phase)
    if target.is_file() and (size is None or target.stat().st_size == size) and sha(target) == expected:
        report(target.stat().st_size,target.stat().st_size,phase='cached',cached=True,verified=True)
        return
    target.parent.mkdir(parents=True, exist_ok=True)
    partial = target.with_name(target.name + '.part')
    for retry in range(3):
        try:
            have = partial.stat().st_size if partial.exists() else 0
            if size is not None and have >= size and partial.exists():
                report(have,size,phase='verifying')
                if have == size and sha(partial) == expected:
                    os.replace(partial,target)
                    report(have,size,phase='verified',verified=True)
                    return
                partial.unlink()
                have = 0
            report(have,size,phase='connecting')
            headers = {'User-Agent': 'AI-Image-Toolbox/LocalAI', 'Accept-Encoding': 'identity'}
            if have:
                headers['Range'] = f'bytes={have}-'
            with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=45) as response:
                if response.status != 206:
                    have = 0
                else:
                    match = re.fullmatch(r'bytes\s+(\d+)-(\d+)/(\d+|\*)',response.headers.get('Content-Range',''))
                    if not match or int(match[1]) != have or int(match[2]) < have:
                        raise IOError('Invalid download range')
                length = response.headers.get('Content-Length')
                total = size if size is not None else (int(match[3]) if response.status==206 and match[3]!='*' else have+int(length) if length is not None else None)
                initial = have
                started = updated = time.monotonic()
                previous = have
                read = getattr(response,'read1',response.read)
                with open(partial, 'ab' if have else 'wb') as stream:
                    while True:
                        block = read(64 * 1024)
                        if not block:
                            break
                        stream.write(block)
                        have += len(block)
                        now = time.monotonic()
                        if now - updated >= .4:
                            report(have,total,(have-previous)/max(.001,now-updated))
                            updated, previous = now, have
            if size is not None and have != size:
                raise IOError('Incomplete download')
            if total is not None and have != total:
                raise IOError('Incomplete download')
            speed = (have-initial)/max(.001,time.monotonic()-started)
            report(have,total,speed,phase='verifying',transferred=have-initial)
            if sha(partial) != expected:
                partial.unlink()
                raise IOError('Download SHA-256 mismatch')
            os.replace(partial, target)
            report(have,have,speed,phase='verified',verified=True,transferred=have-initial)
            return
        except Exception:
            if retry == 2:
                raise
            report(partial.stat().st_size if partial.exists() else 0,size,phase='retry')
            time.sleep(retry + 1)

def extract(archive, root):
    with zipfile.ZipFile(archive) as source:
        for item in source.infolist():
            destination = inside(root, item.filename)
            if item.is_dir():
                destination.mkdir(parents=True, exist_ok=True)
            else:
                destination.parent.mkdir(parents=True, exist_ok=True)
                with source.open(item) as stream, open(destination, 'wb') as output:
                    shutil.copyfileobj(stream, output)

def has_nvidia():
    if sys.platform != 'win32':
        return False
    class Display(ctypes.Structure):
        _fields_ = [('cb', ctypes.c_ulong), ('name', ctypes.c_wchar * 32), ('description', ctypes.c_wchar * 128), ('flags', ctypes.c_ulong), ('id', ctypes.c_wchar * 128), ('key', ctypes.c_wchar * 128)]
    for index in range(20):
        device = Display()
        device.cb = ctypes.sizeof(device)
        if not ctypes.windll.user32.EnumDisplayDevicesW(None, index, ctypes.byref(device), 0):
            break
        if 'nvidia' in device.description.lower():
            return True
    return False

def pip(python, arguments, mirror=False):
    command = [str(python), '-m', 'pip', 'install', '--disable-pip-version-check', '--no-warn-script-location', '--isolated'] + arguments
    if mirror and '--index-url' not in arguments:
        command += ['--index-url', 'https://pypi.tuna.tsinghua.edu.cn/simple']
    emit('progress', message='Installing local inference components / 安装本地推理组件')
    emit('environment',phase='installing',total_bytes=None,percent=None)
    result = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8', errors='replace')
    if result.returncode:
        if mirror and '--index-url' not in arguments:
            return pip(python, arguments, False)
        raise RuntimeError(result.stdout[-5000:])

def inference_environment(request=None):
    """Keep runtime selection explicit without deriving any model/cache paths."""
    request = request or {}
    environment = request.get('environment') or ('cpu' if request.get('backend') == 'cpu' else 'cuda')
    if environment not in ('cpu', 'cuda'):
        raise ValueError('Unsupported inference environment / 不支持的推理环境')
    return environment

def bootstrap(request):
    environment = inference_environment(request)
    root = Path(request['runtime']).resolve()
    root.mkdir(parents=True, exist_ok=True)
    emit('environment',phase='preparing',total_bytes=None,percent=None)
    python = root / 'python' / 'python.exe'
    def atomic_file(target, content):
        partial = target.with_name(target.name + f'.{os.getpid()}.{threading.get_ident()}.repair.part')
        try:
            partial.write_bytes(content)
            os.replace(partial, target)
        finally:
            partial.unlink(missing_ok=True)
    def repair_archive(archive, destination, skip=()):
        with zipfile.ZipFile(archive) as source:
            for item in source.infolist():
                target = inside(destination, item.filename)
                if item.is_dir():
                    target.mkdir(parents=True, exist_ok=True)
                    continue
                if item.filename in skip:
                    continue
                content = source.read(item)
                digest = hashlib.sha256(content).hexdigest()
                if target.is_file() and target.stat().st_size == len(content) and sha(target) == digest:
                    continue
                target.parent.mkdir(parents=True, exist_ok=True)
                atomic_file(target, content)
    # An interrupted extraction can leave python.exe while DLLs, the stdlib zip
    # or extension modules are absent. Repair from the verified pinned archive;
    # leave matching files untouched, including the currently running exe/DLL.
    download(PYTHON_URL, root / 'python.zip', PYTHON_SHA)
    with zipfile.ZipFile(root / 'python.zip') as archive:
        if not {'python.exe', 'python311.dll', 'python311.zip'}.issubset(archive.namelist()):
            raise IOError('Incomplete Python runtime archive')
    repair_archive(root / 'python.zip', python.parent, ('python311._pth',))
    # Restore private search paths every preparation; never modify system PATH.
    search_paths = python.parent / 'python311._pth'
    private_paths = b'python311.zip\n.\nLib/site-packages\nimport site\n'
    if not search_paths.is_file() or search_paths.read_bytes() != private_paths:
        atomic_file(search_paths, private_paths)
    site = python.parent / 'Lib' / 'site-packages'
    # A pip directory alone also does not prove that a prior unpack completed.
    download(PIP_URL, root / 'pip.whl', PIP_SHA)
    repair_archive(root / 'pip.whl', site)
    mirror = dependency_mirror(request)
    packages = [f'{name}=={version}' for name, version in VERSIONS.items() if name != 'torch']
    cuda=environment == 'cuda' and has_nvidia()
    index='https://download.pytorch.org/whl/cu128' if cuda else 'https://download.pytorch.org/whl/cpu'
    # Pin the build suffix as well: pip otherwise accepts an existing CPU wheel
    # with the same version when the user repairs a CUDA installation.
    pip(python, ['torch==2.8.0'+('+cu128' if cuda else '+cpu'), '--index-url', index], mirror)
    try:
        pip(python, packages, mirror)
    except RuntimeError:
        if mirror or request.get('source','auto')!='auto':
            raise
        emit('progress',message='Trying alternate dependency source / 正在切换备用依赖下载源')
        pip(python,packages,True)
    emit('environment',phase='verifying',total_bytes=None,percent=None)
    problems=[]
    for name,version in VERSIONS.items():
        try:
            if importlib.metadata.version(name).split('+')[0]!=version:problems.append(name)
        except importlib.metadata.PackageNotFoundError:problems.append(name)
    if problems:raise RuntimeError('Environment verification failed / 环境校验未通过: '+', '.join(problems))
    emit('environment',phase='ready',total_bytes=None,percent=None)
    emit('result', python=str(python),ready=True,environment=environment)

def policy(total_gb, free_gb, family='sd15', preference='auto'):
    quality = total_gb >= 16 and free_gb >= 12 and preference != 'low'
    low = total_gb <= 8.5 or free_gb < 7 or preference == 'low'
    edge = 1024 if quality else 768 if total_gb >= 7 else 512
    steps = 40 if quality else 32 if total_gb >= 8 else 24
    if family == 'sdxl':
        edge = 1024 if quality else 768
    return {'family': family, 'edge': edge, 'steps': steps, 'offload': total_gb < 16 or free_gb < 12 or preference == 'low', 'sequential': free_gb < 5 and total_gb > 0, 'quality': quality}

def probe(request=None):
    environment = inference_environment(request)
    missing = []
    for name, version in VERSIONS.items():
        try:
            current = importlib.metadata.version(name).split('+')[0]
            if current != version:
                missing.append(name)
        except importlib.metadata.PackageNotFoundError:
            missing.append(name)
    result = {'missing': missing, 'cuda': False, 'total_gb': 0, 'free_gb': 0, 'device': 'CPU', 'environment': environment}
    if not missing:
        import torch
        if environment == 'cpu' and torch.version.cuda is not None:
            missing.append('torch CPU')
        elif environment == 'cuda' and torch.version.cuda is None and has_nvidia():
            missing.append('torch CUDA')
        if environment == 'cuda' and torch.cuda.is_available():
            free, total = torch.cuda.mem_get_info()
            result.update(cuda=True, total_gb=total / 2**30, free_gb=free / 2**30, device=torch.cuda.get_device_name(0))
    result['policy'] = policy(result['total_gb'], result['free_gb'])
    emit('result', **result)

class ModelProgress:
    """Aggregate required files, including cache/reuse and unverified in-flight bytes.

    downloaded_bytes is overall work already present, not network traffic. A
    received or hashed file is complete only after its accepted path verifies.
    """
    def __init__(self,packages):
        self.sizes={p['target']+'/'+f['local']:f['size'] for p in packages for f in p['files']}
        self.bytes={label:0 for label in self.sizes};self.completed=set();self.lock=threading.RLock()
        self.total=sum(self.sizes.values());self.phase='checking';self.last=None;self.publish('checking')
    def publish(self,phase=None):
        with self.lock:
            if phase:self.phase=phase
            have=sum(self.bytes.values());ready=self.phase=='ready';verified=sum(self.sizes[k] for k in self.completed)
            snapshot=(self.phase,len(self.completed),have,verified)
            if snapshot==self.last:return
            self.last=snapshot
            emit('overall',phase=self.phase,completed_files=len(self.completed),total_files=len(self.sizes),
                 downloaded_bytes=have,total_bytes=self.total,verified_bytes=verified,
                 percent=100 if ready else min(99.9,have*100/self.total) if self.total else 0,ready=ready)
    def update(self,label,have,phase):
        with self.lock:
            if label not in self.sizes:return
            self.bytes[label]=min(self.sizes[label],max(0,have))
            if phase=='invalid':self.completed.discard(label)
            elif phase not in ('cached','verified'):self.completed.discard(label)
            self.publish('verifying' if phase in ('verified','cached','verifying','invalid') else 'downloading')
    def complete(self,label):
        with self.lock:
            self.completed.add(label);self.bytes[label]=self.sizes[label];self.publish('verifying')
    def finish(self,ready):
        if ready and len(self.completed)!=len(self.sizes):raise IOError('Not all required model files are verified')
        self.publish('ready' if ready else 'incomplete')

def required_model_packages(request):
    manifest = json.loads(Path(request['manifest']).read_text(encoding='utf-8'))
    family = request.get('family', 'sd15')
    packages = [p for p in manifest['packages'] if p['family'] in (family, 'shared')]
    skip=request.get('skip_weights',[])
    if request.get('configuration_only'):
        skip=['base','controlnet']
    packages=[dict(p,source_target=p['target'],target='configs/'+('controlnet-' if p['target'].startswith('controlnet/') else '')+family,files=[f for f in p['files'] if not f['local'].endswith(('.bin','.safetensors'))]) if p['target'].split('/')[0] in skip else p for p in packages]
    return packages

def download_models(request):
    packages=required_model_packages(request);progress=ModelProgress(packages)
    try:return _download_models(request,packages,progress)
    except BaseException:
        progress.publish('failed');raise

def _download_models(request,packages,progress):
    family=request.get('family','sd15')
    root = Path(request['models']).resolve()
    root.mkdir(parents=True, exist_ok=True)
    emit('progress',message='Checking cached model files / 校验已缓存模型文件')
    verifier=ModelVerifier(root,request.get('force_hash',False),progress.update)
    roots=[root]+[Path(p) for p in request.get('search_roots',[]) if isinstance(p,str) and p]
    reuse={'version':1,'packages':{}}
    try:
        saved=json.loads(inside(root,'model-reuse.json').read_text(encoding='utf-8'))
        if isinstance(saved,dict) and isinstance(saved.get('packages'),dict):reuse['packages']=saved['packages']
    except (OSError,ValueError,AttributeError):pass
    pending = [];referenced=[];hardlinked=0;blocked=[];accepted={}
    def accept(package,file,path):
        label=package['target']+'/'+file['local'];accepted[label]=Path(path);progress.complete(label)
    def ready():
        # A final complete pass also catches an earlier cache/reuse file that
        # changed while another file was downloading. Never signal ready from
        # downloaded lengths, a successful pip exit, or a partial manifest.
        for package in packages:
            for file in package['files']:
                label=package['target']+'/'+file['local'];path=accepted.get(label)
                if path is None or not verifier.verify(path,file,label):raise IOError('Required model verification failed / 必需模型文件校验失败: '+label)
                progress.complete(label)
        verifier.save();progress.finish(True)
    for package in packages:
        label=package['target'];dest=inside(root,label)
        candidates=package_candidates(package,roots)
        prior=reuse['packages'].pop(label,None)
        if isinstance(prior,dict) and isinstance(prior.get('path'),str):
            old=Path(prior['path']).resolve()
            if old.is_dir() and old not in candidates:candidates.insert(0,old)
        # A complete external package is referenced as a directory. Never
        # install, repair, or create partial files in that external location.
        local_ok=all(verifier.verify(dest/f['local'],f,label+'/'+f['local']) for f in package['files'])
        complete=None
        if not local_ok:
            for candidate in candidates:
                if candidate==dest:continue
                if all(verifier.verify(candidate/f['local'],f,label+'/'+f['local']) for f in package['files'] if not optional_model_document(f)):complete=candidate;break
        if complete is not None:
            reuse['packages'][label]={'path':str(complete),'family':package['family'],'files':package['files']}
            referenced.append(label)
            emit('progress',message='Reusing verified external model / 引用已校验外部模型: '+str(complete))
            for file in package['files']:
                if not optional_model_document(file):
                    accept(package,file,complete/file['local']);continue
                target=inside(root,label+'/'+file['local'])
                if verifier.verify(target,file,label+'/'+file['local']):
                    accept(package,file,target)
                else:
                    # Inference packages can omit tiny documentation. Keep the
                    # official docs in the owned Root, never the external folder.
                    source=complete/file['local'];target=inside(root,label+'/'+file['local'])
                    if verifier.verify(source,file,label+'/'+file['local']):
                        target.parent.mkdir(parents=True,exist_ok=True)
                        temporary=target.with_name(target.name+f'.{os.getpid()}.reuse.part')
                        try:
                            shutil.copyfile(source,temporary);os.replace(temporary,target)
                            if not verifier.verify(target,file,label+'/'+file['local']):raise IOError('Copied model document verification failed')
                            accept(package,file,target)
                        finally:temporary.unlink(missing_ok=True)
                    else:pending.append((package,file))
            continue
        for file in package['files']:
            target = inside(root,package['target']+'/'+file['local'])
            okay=verifier.verify(target,file,label+'/'+file['local'])
            partial=inside(root,package['target']+'/'+file['local']+'.part')
            if not okay and verifier.verify(partial,file,label+'/'+file['local']):
                os.replace(partial,target);okay=True
            if not okay:
                for candidate in candidates:
                    source=candidate/file['local']
                    if source.resolve()==target:continue
                    if not verifier.verify(source,file,label+'/'+file['local']):continue
                    target.parent.mkdir(parents=True,exist_ok=True)
                    staged=target.with_name(target.name+f'.{os.getpid()}.reuse.part')
                    try:
                        # os.link fails safely across volumes; never copy GB
                        # weights as an implicit fallback. Repair uses replace.
                        os.link(source.resolve(),staged)
                        if not verifier.verify(staged,file,label+'/'+file['local']):raise IOError('Source changed before reuse')
                        os.replace(staged,target);okay=True;hardlinked+=1
                        emit('progress',message='Reusing verified model file without copying / 已校验模型文件零复制复用: '+label+'/'+file['local'])
                        break
                    except OSError:
                        if file['local'].endswith(('.safetensors','.bin','.onnx')):
                            blocked.append(label+'/'+file['local'])
                            emit('progress',message='Cannot reuse partial model across disks; select a complete model directory or use the existing model disk / 外部半完整模型无法跨盘零复制复用，请选择完整模型目录或将存储目录设到现有模型所在盘')
                        elif file['size']<=65536:
                            shutil.copyfile(source,staged)
                            if verifier.verify(staged,file,label+'/'+file['local']):os.replace(staged,target);okay=True;break
                    finally:staged.unlink(missing_ok=True)
            if okay:
                blocked=[name for name in blocked if name!=label+'/'+file['local']]
                accept(package,file,target)
                emit('download',file=package['target']+'/'+file['local'],bytes=file['size'],total=file['size'],speed=0,percent=100,phase='cached',cached=True,verified=True)
            else:
                pending.append((package,file))
    verifier.save();atomic_json(inside(root,'model-reuse.json'),reuse)
    if blocked and not request.get('inspection_only'):
        raise IOError('Existing weights were not copied or redownloaded. Select a complete model directory or use the existing model disk / 已有权重未复制或重复下载，请选择完整模型目录或将存储目录设到现有模型所在盘')
    if request.get('inspection_only'):
        if not pending:ready()
        else:progress.finish(False)
        emit('result',models=str(root),family=family,cached=not pending,missing=len(pending),referenced=referenced,hardlinked=hardlinked,ready=not pending)
        return
    if not pending:
        ready();emit('result',models=str(root),family=family,cached=True,referenced=referenced,hardlinked=hardlinked,ready=True)
        return
    needed = sum(max(0,f['size']-min(f['size'],inside(root,p['target']+'/'+f['local']+'.part').stat().st_size if inside(root,p['target']+'/'+f['local']+'.part').is_file() else 0)) for p,f in pending)
    if shutil.disk_usage(root).free < needed + 2**30:
        raise IOError('Not enough free disk space / 磁盘可用空间不足')
    package,file = min(pending,key=lambda item:item[1]['size'])
    suffix = f"/{package['repo']}/resolve/{package['revision']}/{file['path']}"
    # Probe a small pinned manifest file, never a multi-gigabyte weight file.
    if file['size']>16384:
        small = [(p,f) for p in packages for f in p['files'] if f['size']<=16384]
        if small:
            package,file = min(small,key=lambda item:item[1]['size'])
            suffix = f"/{package['repo']}/resolve/{package['revision']}/{file['path']}"
        else:
            suffix = '/api/models/Lykon/dreamshaper-8'
    domains = choose_sources(request,'https://huggingface.co','https://hf-mirror.com',suffix)
    emit('progress', message=f'Model files / 模型文件: {needed / 2**30:.1f} GiB, {family}')
    # Resolve and create owned destination parents before worker threads start.
    # In particular, concurrent first writes must not resolve a parent while
    # another thread is creating that parent hierarchy. Recheck containment
    # after mkdir; references and external candidate paths are never created.
    download_targets={}
    for package,file in pending:
        label=package['target']+'/'+file['local'];target=inside(root,label)
        target.parent.mkdir(parents=True,exist_ok=True);download_targets[label]=inside(root,label)
    def item(package, file):
        target = download_targets[package['target']+'/'+file['local']]
        last = None
        for domain in domains:
            try:
                url = f"{domain}/{package['repo']}/resolve/{package['revision']}/{file['path']}"
                download(url, target, file['sha256'], file['size'],package['target']+'/'+file['local'],progress=progress.update)
                if not verifier.verify(target,file,package['target']+'/'+file['local']):raise IOError('Downloaded model verification failed')
                accept(package,file,target)
                return
            except Exception as error:
                last = error
                if domain!=domains[-1]:emit('progress',message='Trying alternate model source / 正在切换备用模型下载源')
        raise last
    with ThreadPoolExecutor(max_workers=3) as pool:
        jobs = [pool.submit(item, p, f) for p,f in pending]
        for future in jobs:
            future.result()
    # Record only successfully written final files; next process may reuse
    # fingerprints, while the explicit Check action forces full hashing.
    verifier.force=False
    for package,file in pending:
        if not verifier.verify(inside(root,package['target']+'/'+file['local']),file,package['target']+'/'+file['local']):raise IOError('Downloaded model verification failed')
    ready()
    emit('result', models=str(root), family=family,ready=True)

def caption(image, root, device):
    import torch
    from transformers import BlipProcessor, BlipForConditionalGeneration
    path = package_path(root,'caption/blip-base')
    emit('progress', message='Reading the image locally / 本地识别画面')
    processor = BlipProcessor.from_pretrained(str(path), local_files_only=True)
    model = BlipForConditionalGeneration.from_pretrained(str(path), local_files_only=True).to(device)
    inputs = None
    try:
        inputs = processor(images=image, return_tensors='pt').to(device)
        with torch.inference_mode():
            tokens = model.generate(**inputs, max_new_tokens=50, num_beams=3)
        return processor.decode(tokens[0], skip_special_tokens=True)
    finally:
        del inputs, model, processor
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

def visual_style(description):
    content = description.lower()
    if any(word in content for word in ('anime', 'cartoon', 'drawing', 'illustration')):
        return 'illustration, clean coherent linework, matching original shading'
    if any(word in content for word in ('photo', 'photograph', 'photography')):
        return 'photograph, realistic surface detail, natural lighting'
    return 'matching the original visual style, consistent colors and lighting'

def caption_has(description, terms):
    """Match caption words and phrases, never substrings such as tree/street."""
    import re
    return any(re.search(r'(?<!\w)'+re.escape(term)+r'(?:s|es)?(?!\w)',description.lower()) for term in terms)


def background_prompt(description):
    content = description.lower()
    if caption_has(content,('city','cities','street','building','architecture')):
        scene = 'coherent street architecture, varied windows, layered balconies, reflected daylight'
    elif caption_has(content,('forest','tree','garden','plant')) and not caption_has(content,('room','window','indoor')):
        scene = 'overlapping leaves and branches, varied foliage, dappled daylight'
    elif caption_has(content,('sea','beach','ocean','water')):
        scene = 'coastal scenery, rippling water, rocks and soft reflected daylight'
    elif caption_has(content,('mountain','landscape','hill')):
        scene = 'layered landscape, textured rocks and foliage, natural depth and soft shadows'
    else:
        scene = 'quiet room, wooden window frames, a few leafy plants, soft side lighting'
    return f'{scene}, {visual_style(description)}'

def make_prompt(description, local_description=''):
    # Treat captions as scene descriptions, never as arbitrary generation directives.
    content = (local_description + ' ' + description).lower()
    garment=caption_has(content,('sweater','vest','dress','shirt','fabric','coat','clothing','uniform','jacket','garment'))
    if garment:
        material = 'original clothing design and colors, existing fabric weave, natural garment folds'
        if caption_has(content,('knit','knitted','sweater','wool')):
            material += ', existing knitted wool yarn'
        # Caption colors may describe hair/skin/accessories, or be hallucinated.
        # The source image/Tile control supplies the garment palette directly.
        material += ', original fabric palette'
        for pattern in ('leopard','animal print','floral','striped','plaid','polka dot'):
            if caption_has(content,(pattern,)):material += ', existing '+pattern+' fabric pattern'
    else:
        material = 'coherent surface detail, layered natural light and shadow'
    if caption_has(content,('city','cities','building','street','house','architecture')):
        material += ', existing architecture, varied window frames, reflections, coherent building details'
    if caption_has(content,('tree','forest','garden','plant','foliage','leaf','leaves','blossom')) or (not garment and caption_has(content,('flower',))):
        material += ', existing foliage, overlapping leaves and branches, natural foliage detail'
    # Avoid directing generation with guessed actions, accessories or identities.
    return f'preserve the original scene and subject, {material}, {visual_style(description + " " + local_description)}'


def detail_caption_regions(source,condition,materials,limit=2):
    """Find textured nonhuman crop regions instead of captioning only empty sky."""
    import numpy as np
    grid=np.asarray(condition)[:,:,0];rows,cols=np.where(grid!=128)
    if not len(rows):return []
    left,top,right,bottom=int(cols.min()),int(rows.min()),int(cols.max())+1,int(rows.max())+1
    edge=min(288,right-left,bottom-top)
    protected=np.isin(np.asarray(materials),(2,3,11,12,13,14,15))
    luminance=np.asarray(source.convert('L'),dtype=np.float32)
    choices=[]
    for y in np.linspace(top,bottom-edge,4).astype(int):
        for x in np.linspace(left,right-edge,4).astype(int):
            if protected[y:y+edge,x:x+edge].mean()>.03:continue
            pixels=luminance[y:y+edge,x:x+edge]
            score=float(abs(np.diff(pixels,axis=0)).mean()+abs(np.diff(pixels,axis=1)).mean())
            choices.append((score,(int(x),int(y),int(x+edge),int(y+edge))))
    selected=[]
    for score,box in sorted(choices,reverse=True):
        if score<2:break
        if any(abs(box[0]-old[0])<edge*.6 and abs(box[1]-old[1])<edge*.6 for old in selected):continue
        selected.append(box)
        if len(selected)>=limit:break
    return [source.crop(box) for box in selected]

def contrast_refinements(image, condition):
    """Constrain module luminance smoothly while retaining generated color/texture."""
    import math
    import numpy as np
    from PIL import Image
    grid = np.asarray(condition)[:, :, 0]
    rows, cols = np.where(grid != 128)
    if not len(rows):
        return
    left, top, right, bottom = int(cols.min()), int(rows.min()), int(cols.max()) + 1, int(rows.max()) + 1
    size = min(right - left, bottom - top)
    units = []
    for line in grid[top:bottom, left:right]:
        changes = np.flatnonzero(np.diff(line.astype(np.int16))) + 1
        units.extend(np.diff(np.r_[0, changes, size]).tolist())
    scale = 0
    for run in units:
        scale = math.gcd(scale, int(run))
    if scale < 3:
        return
    count = size // scale
    original = np.asarray(image, dtype=np.float32) / 255
    for level in (.28, .4, .55, .72):
        result = original.copy()
        yy, xx = np.mgrid[:scale, :scale]
        distance = np.maximum(np.abs((xx + .5) / scale - .5), np.abs((yy + .5) / scale - .5))
        influence = np.clip((level / 2 + .12 - distance) / .12, 0, 1)[..., None]
        for row in range(count):
            for col in range(count):
                y, x = top + row * scale, left + col * scale
                area = result[y:y + scale, x:x + scale]
                ink = grid[y + scale // 2, x + scale // 2] < 128
                luminance = (area * np.array([.299, .587, .114])).sum(axis=2, keepdims=True)
                target = .18 if ink else .82
                functional = (col < 8 and row < 8) or (col >= count - 8 and row < 8) or (col < 8 and row >= count - 8) or col == 6 or row == 6
                amount = np.ones_like(influence) if functional else influence
                if ink:
                    corrected = area * np.minimum(1, target / np.maximum(luminance, .001))
                else:
                    corrected = area + (1 - area) * np.maximum(0, (target - luminance) / np.maximum(1 - luminance, .001))
                result[y:y + scale, x:x + scale] = area * (1 - amount) + corrected * amount
        yield Image.fromarray(np.clip(result * 255, 0, 255).astype(np.uint8)), level

def preserve_surroundings(image, source, condition):
    import numpy as np
    from PIL import Image, ImageDraw, ImageFilter
    grid = np.asarray(condition)[:, :, 0]
    rows, cols = np.where(grid != 128)
    if not len(rows):
        return image
    mask = Image.new('L', image.size)
    ImageDraw.Draw(mask).rectangle((int(cols.min()) - 72, int(rows.min()) - 72, int(cols.max()) + 72, int(rows.max()) + 72), fill=255)
    mask = mask.filter(ImageFilter.GaussianBlur(8))
    return Image.composite(image, source, mask)

def blank_background_mask(image):
    """Only whiteness connected to the canvas border counts as background."""
    from collections import deque
    import numpy as np
    from PIL import Image, ImageFilter
    small = image.copy()
    small.thumbnail((192, 192))
    pixels = np.asarray(small)
    blank = (pixels.min(axis=2) >= 242) & (pixels.max(axis=2) - pixels.min(axis=2) <= 12)
    h, w = blank.shape
    found = np.zeros((h, w), dtype=np.uint8)
    queue = deque([(0, x) for x in range(w)] + [(h-1, x) for x in range(w)] + [(y, 0) for y in range(h)] + [(y, w-1) for y in range(h)])
    while queue:
        y, x = queue.popleft()
        if not (0 <= y < h and 0 <= x < w) or found[y,x] or not blank[y,x]:
            continue
        found[y,x] = 255
        queue.extend(((y-1,x),(y+1,x),(y,x-1),(y,x+1)))
    if (found > 0).mean() < .15:
        return None
    # Keep bright clothing/eyes that are enclosed by the subject untouched.
    return Image.fromarray(found).resize(image.size, Image.Resampling.BILINEAR).filter(ImageFilter.GaussianBlur(1))

def parse_materials(image, root, device='cpu', tiled=True, protected_confidence=0.):
    import numpy as np
    import torch
    from PIL import Image
    from transformers import SegformerImageProcessor, SegformerForSemanticSegmentation
    path = package_path(root,'segmentation/clothes')
    processor = SegformerImageProcessor.from_pretrained(str(path), local_files_only=True)
    model = SegformerForSemanticSegmentation.from_pretrained(str(path), local_files_only=True).to(device)
    emit('progress', message='Separating skin, hair, clothes and background locally / 本地识别皮肤、头发、衣物与背景')
    try:
        inputs = processor(images=image, return_tensors='pt').to(device)
        with torch.inference_mode():
            logits = model(**inputs).logits
            upsampled = torch.nn.functional.interpolate(logits, size=(image.height,image.width), mode='bilinear', align_corners=False)
            labels = upsampled.argmax(dim=1)[0].cpu().numpy().astype(np.uint8)
            if protected_confidence>0:
                probability=upsampled.softmax(1).max(1).values[0].cpu().numpy()
                uncertain=np.isin(labels,(2,3,11,12,13,14,15))&(probability<protected_confidence)
                labels[uncertain]=0
        if tiled and max(image.size)>768:
            edge=min(768,min(image.size));step=max(1,edge*3//4)
            xs=sorted(set(range(0,image.width-edge+1,step))|{image.width-edge})
            ys=sorted(set(range(0,image.height-edge+1,step))|{image.height-edge})
            for top in ys:
                for left in xs:
                    crop=image.crop((left,top,left+edge,top+edge))
                    inputs=processor(images=crop,return_tensors='pt').to(device)
                    with torch.inference_mode():
                        logits=model(**inputs).logits
                        logits=torch.nn.functional.interpolate(logits,size=(edge,edge),mode='bilinear',align_corners=False)
                        probability,index=logits.softmax(1).max(1)
                    found=index[0].cpu().numpy().astype(np.uint8)
                    confident=probability[0].cpu().numpy()>.7
                    protected=np.isin(found,(2,3,11,12,13,14,15))&confident
                    region=labels[top:top+edge,left:left+edge]
                    region[protected]=found[protected]
        return Image.fromarray(labels).resize(image.size, Image.Resampling.NEAREST)
    finally:
        del model, processor
        gc.collect()
        if torch.cuda.is_available():
            torch.cuda.empty_cache()

def material_masks(labels):
    import numpy as np
    from PIL import Image, ImageFilter
    values = np.asarray(labels)
    protected = np.isin(values, (2,3,11,12,13,14,15))
    # A small guard band prevents generated hair or skin colors from crossing edges.
    guard = Image.fromarray((protected*255).astype(np.uint8)).filter(ImageFilter.MaxFilter(11))
    allowed = np.asarray(guard) == 0
    background = (values == 0) & allowed
    cloth = ~protected & (values != 0) & allowed
    def soften(values):
        soft=Image.fromarray((values*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))
        return Image.fromarray(np.where(allowed,np.asarray(soft),0).astype(np.uint8))
    return soften(background), soften(cloth)


def combined_material_mask(labels):
    """Union of editable materials without fading at garment/background seams."""
    import numpy as np
    from PIL import Image
    background,clothing=material_masks(labels)
    # Each soft mask contributes its coverage at a shared semantic boundary.
    # Taking their maximum would leave a half-transparent stripe there.
    coverage=np.asarray(background,dtype=np.uint16)+np.asarray(clothing,dtype=np.uint16)
    return Image.fromarray(np.minimum(coverage,255).astype(np.uint8))

def manual_skin_mask(labels, condition):
    """Allow limb skin only inside the requested QR area, keeping identity/edges."""
    import numpy as np
    from PIL import Image, ImageFilter
    if labels.size != condition.size:
        raise ValueError('Skin masks require matching dimensions')
    values=np.asarray(labels)
    skin=Image.fromarray(np.isin(values,(12,13,14,15)).astype(np.uint8)*255)
    # Keep the original silhouette and the face/hair/mosaic guard. A hard face
    # guard is independent of the selected position; manual placement never moves it.
    interior=np.asarray(skin.filter(ImageFilter.MinFilter(7)))>0
    identity=Image.fromarray(np.isin(values,(2,3,11)).astype(np.uint8)*255).filter(ImageFilter.MaxFilter(11))
    code=np.asarray(condition.convert('L'))!=128
    allowed=interior&(np.asarray(identity)==0)&code
    soft=Image.fromarray(allowed.astype(np.uint8)*255).filter(ImageFilter.GaussianBlur(1))
    return Image.fromarray(np.where(allowed,np.asarray(soft),0).astype(np.uint8))

def editable_material_mask(labels, skin=None):
    """Keep the existing automatic material mask unchanged unless opted in."""
    from PIL import ImageChops
    original=combined_material_mask(labels)
    return ImageChops.lighter(original,skin) if skin is not None else original

def constrained_skin_light(image, source, skin, max_delta=64.):
    """Retain skin hue/detail with a strict final, quantized source-relative Y bound."""
    import numpy as np
    from PIL import Image, ImageFilter
    if image.size != source.size or skin.size != source.size:
        raise ValueError('Skin light requires matching dimensions')
    coverage=np.asarray(skin,dtype=np.float32)/255.
    if not np.any(coverage):
        return image
    original=source.convert('YCbCr').split()[0]
    generated=image.convert('YCbCr').split()[0]
    # Generated high-frequency marks cannot replace source pores/linework.
    delta=np.asarray(generated.filter(ImageFilter.GaussianBlur(1)),dtype=np.float32)-np.asarray(original.filter(ImageFilter.GaussianBlur(1)),dtype=np.float32)
    bound=max(0.,min(64.,float(max_delta)))
    if bound==0:
        output=np.asarray(image.convert('RGB')).copy()
        output[coverage>0]=np.asarray(source.convert('RGB'))[coverage>0]
        return Image.fromarray(output)
    luminance=np.asarray(original,dtype=np.float32)+np.clip(delta,-bound,bound)*coverage
    neutral=Image.fromarray(np.clip(luminance,0,255).round().astype(np.uint8)).convert('RGB')
    flesh=matching_palette(neutral,source)
    output=np.asarray(image.convert('RGB')).copy()
    selected=coverage>0
    output[selected]=np.asarray(flesh)[selected]
    # RGB gamut clipping and integer YCbCr conversion can move the actual Y by
    # two levels. Check the final RGB, rather than trusting the intermediate Y.
    # Keep the soft-mask limit as well as the full max_delta limit. A neutral
    # offset usually fixes rounding while preserving hue; saturated pixels that
    # cannot converge within four small corrections retain the exact source RGB.
    source_y=np.asarray(original,dtype=np.int16)
    limit=bound*(np.asarray(skin,dtype=np.float64)/255.)
    lower=np.ceil(source_y-limit).clip(0,255).astype(np.int16)
    upper=np.floor(source_y+limit).clip(0,255).astype(np.int16)
    for _ in range(4):
        actual_y=np.asarray(Image.fromarray(output).convert('YCbCr'))[:,:,0].astype(np.int16)
        correction=np.clip(actual_y,lower,upper)-actual_y
        outside=selected&(correction!=0)
        if not outside.any():
            break
        corrected=output[outside].astype(np.int16)+correction[outside,None]
        output[outside]=corrected.clip(0,255).astype(np.uint8)
    actual_y=np.asarray(Image.fromarray(output).convert('YCbCr'))[:,:,0].astype(np.int16)
    outside=selected&((actual_y<lower)|(actual_y>upper))
    output[outside]=np.asarray(source.convert('RGB'))[outside]
    return Image.fromarray(output)

def face_guards(image, root, confidence=.3):
    """Detect faces locally; keep expanded face boxes as original pixels."""
    import numpy as np
    import onnxruntime as ort
    from PIL import Image, ImageDraw
    model=package_path(root,'protection/anime-face')/'model.onnx'
    if not model.is_file():
        raise ValueError('Repair local models to install face protection / 请修复本地模型，安装人脸保护模型')
    options=ort.SessionOptions();options.intra_op_num_threads=2
    session=ort.InferenceSession(str(model),options,providers=['CPUExecutionProvider'])
    small=image.copy();small.thumbnail((640,640),Image.Resampling.LANCZOS)
    offset=((640-small.width)//2,(640-small.height)//2)
    canvas=Image.new('RGB',(640,640),(114,114,114));canvas.paste(small,offset)
    prediction=session.run(None,{session.get_inputs()[0].name:np.asarray(canvas).astype(np.float32).transpose(2,0,1)[None]/255})[0][0].T
    candidates=prediction[prediction[:,4]>confidence];candidates=candidates[np.argsort(-candidates[:,4])]
    boxes=[]
    for cx,cy,w,h,confidence in candidates:
        box=np.array([cx-w/2,cy-h/2,cx+w/2,cy+h/2])
        if any((lambda overlap:overlap[0]*overlap[1]/max(1,w*h+(b[2]-b[0])*(b[3]-b[1])-overlap[0]*overlap[1])>.45)(np.maximum(0,np.minimum(box[2:],b[2:])-np.maximum(box[:2],b[:2]))) for b in boxes):continue
        boxes.append(box)
    mask=Image.new('L',image.size);draw=ImageDraw.Draw(mask)
    scale=image.width/small.width
    for left,top,right,bottom in boxes:
        w,h=right-left,bottom-top
        draw.rectangle(((left-offset[0]-w*.2)*scale,(top-offset[1]-h*.45)*scale,(right-offset[0]+w*.2)*scale,(bottom-offset[1]+h*.2)*scale),fill=255)
    emit('progress',message=f'Locking detected faces / 锁定检测到的人脸: {len(boxes)}')
    return mask


def pixelation_mask(image):
    """Conservatively protect dense grids of flat, square color blocks."""
    from collections import deque
    import numpy as np
    from PIL import Image, ImageDraw
    small=image.copy();small.thumbnail((768,768),Image.Resampling.NEAREST)
    pixels=np.asarray(small)
    flat=np.zeros(pixels.shape[:2],dtype=bool)
    flat[1:-1,1:-1]=(pixels[1:-1,1:-1]==pixels[:-2,1:-1]).all(2)&(pixels[1:-1,1:-1]==pixels[2:,1:-1]).all(2)&(pixels[1:-1,1:-1]==pixels[1:-1,:-2]).all(2)&(pixels[1:-1,1:-1]==pixels[1:-1,2:]).all(2)
    visited=np.zeros(flat.shape,dtype=bool);rectangles=[]
    for start in np.argwhere(flat):
        y,x=map(int,start)
        if visited[y,x]:continue
        queue=deque([(y,x)]);visited[y,x]=True;count=0;left=right=x;top=bottom=y
        while queue:
            row,col=queue.popleft();count+=1
            left=min(left,col);right=max(right,col);top=min(top,row);bottom=max(bottom,row)
            for ry,rx in ((row-1,col),(row+1,col),(row,col-1),(row,col+1)):
                if 0<=ry<flat.shape[0] and 0<=rx<flat.shape[1] and flat[ry,rx] and not visited[ry,rx]:
                    visited[ry,rx]=True;queue.append((ry,rx))
        w,h=right-left+1,bottom-top+1
        if min(w,h)>=5 and max(w,h)<=min(small.size)//5 and .75<w/h<1.33 and count/(w*h)>.94:
            rectangles.append((left,top,right+1,bottom+1,w,h))
    mask=Image.new('L',small.size);draw=ImageDraw.Draw(mask)
    for box in rectangles:
        left,top,right,bottom,w,h=box
        neighbors=[b for b in rectangles if abs(b[4]-w)<=2 and abs(b[5]-h)<=2 and abs(b[0]-left)<w*3.6 and abs(b[1]-top)<h*3.6]
        if len(neighbors)>=8:
            draw.rectangle((left-2,top-2,right+1,bottom+1),fill=255)
    bounds=mask.getbbox()
    if bounds:
        # Include edge cells whose clipped/color-merged shape was not square.
        padding=max(b[4] for b in rectangles if b[0]<bounds[2] and b[2]>bounds[0] and b[1]<bounds[3] and b[3]>bounds[1])+2
        draw.rectangle((bounds[0]-padding,bounds[1]-padding,bounds[2]+padding,bounds[3]+padding),fill=255)
    return mask.resize(image.size,Image.Resampling.NEAREST)


def place_away_from_subject(condition, materials):
    import math
    import numpy as np
    from PIL import Image
    grid=np.asarray(condition)[:,:,0]
    yy,xx=np.where(grid!=128)
    original_x,original_y=int(xx.min()),int(yy.min())
    x,y=original_x,original_y
    side=int(xx.max())-x+1
    # Keep four module widths available for the quiet zone on every side.
    module=0
    for line in grid[y:y+side,x:x+side]:
        for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,side]):
            module=math.gcd(module,int(run))
    border=max(16,module*4)
    border=min(border,max(0,(min(condition.size)-side)//2))
    values=np.asarray(materials)
    protected=np.isin(values,(2,3,11,12,13,14,15))
    step=max(8,side//20)
    best=(float('inf'),x,y,0)
    y=max(border,min(condition.height-side-border,y));x=max(border,min(condition.width-side-border,x))
    for top in sorted(set(range(border,condition.height-side-border+1,step))|{y,condition.height-side-border}):
        for left in sorted(set(range(border,condition.width-side-border+1,step))|{x,condition.width-side-border}):
            crop=protected[top:top+side,left:left+side]
            corner=max(8,side//5)
            corners=(crop[:corner,:corner].mean(),crop[:corner,-corner:].mean(),crop[-corner:,-corner:].mean(),crop[-corner:,:corner].mean())
            region=values[top:top+side,left:left+side]
            background=(region==0).mean()
            # Prefer one continuous material, rather than a finder on a wall
            # and two finders on a garment.
            mixed=min(background,1-background)
            common=crop.mean()+mixed*.45+(((left-x)**2+(top-y)**2)**.5)/max(condition.size)*.015
            for rotation,missing in ((0,2),(90,1),(180,0),(270,3)):
                markers=[value for index,value in enumerate(corners) if index!=missing]
                cost=common+sum(markers)/3*4+max(markers)*2
                if cost<best[0]:best=(cost,left,top,rotation)
    _,left,top,rotation=best
    output=Image.new('RGB',condition.size,(128,128,128))
    output.paste(condition.crop((original_x,original_y,original_x+side,original_y+side)).rotate(rotation),(left,top))
    oriented=np.asarray(output)[:,:,0]
    marker_overlap=max((protected[fy:fy+7*module,fx:fx+7*module].mean() for fx,fy in finder_boxes(oriented,left,top,side,module)),default=0.)
    data_overlap=protected[top:top+side,left:left+side].mean()
    locked_overlap=(values[top:top+side,left:left+side]==3).mean()
    return output,dict(x=left,y=top,size=side,width=condition.width,height=condition.height,rotation=rotation,finder_overlap=float(marker_overlap),protected_overlap=float(data_overlap),locked_overlap=float(locked_overlap))


def adaptive_placement(condition,materials,source):
    """Keep full QR modules while fitting the largest region clear of faces."""
    import math
    import numpy as np
    from PIL import Image,ImageOps
    raw=np.asarray(condition)[:,:,0];yy,xx=np.where(raw!=128)
    if not len(xx):return condition,materials,None
    tile=condition.crop((int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1))
    base_size=condition.size;original_labels=materials
    old_content=ImageOps.contain(source,base_size)
    old_left=(base_size[0]-old_content.width)//2;old_top=(base_size[1]-old_content.height)//2
    labels=materials.crop((old_left,old_top,old_left+old_content.width,old_top+old_content.height))
    desired_x=(float(xx.min())+float(xx.max())+1)/2/base_size[0]
    desired_y=(float(yy.min())+float(yy.max())+1)/2/base_size[1]
    size=base_size;best=None
    for attempt in range(9):
        trial=Image.new('RGB',size,(128,128,128))
        trial.paste(tile,(max(0,min(size[0]-tile.width,round(size[0]*desired_x-tile.width/2))),max(0,min(size[1]-tile.height,round(size[1]*desired_y-tile.height/2)))))
        if size==base_size:current=original_labels
        else:
            content=ImageOps.contain(source,size)
            current=Image.new('L',size)
            current.paste(labels.resize(content.size,Image.Resampling.NEAREST),((size[0]-content.width)//2,(size[1]-content.height)//2))
        placed,region=place_away_from_subject(trial,current)
        best=(placed,current,region)
        if region['finder_overlap']<=.005 and region['protected_overlap']<=.12 and region['locked_overlap']<=.002:break
        short=((int(min(size)*1.12)+63)//64)*64
        ratio=base_size[0]/base_size[1]
        size=(math.ceil(short*ratio/64)*64,short) if ratio>=1 else (short,math.ceil(short/ratio/64)*64)
        # The longer side follows the original aspect and stays within the same
        # public limit. Do not crop the source or shrink its QR module pixels.
        if max(size)>3072 or tile.width/min(size)<.30:break
    if best[0].size!=base_size:
        emit('progress',message=f'Adjusting QR coverage to keep protected features clear / 避开锁定人物区域，调整实际码区占短边比例至 {tile.width/min(best[0].size):.0%}；保留完整码点像素')
    return best

def local_material_prompt(prompt, cloth):
    if cloth:
        content=prompt.lower()
        knitted=caption_has(content,('knit','knitted','sweater','wool'))
        fabric='existing ribbed wool yarn' if knitted else 'original garment material and surface detail'
        palette='original fabric colors'
        pattern=next(('existing '+name+' fabric pattern' for name in ('leopard','animal print','floral','striped','plaid','polka dot') if caption_has(prompt,(name,))),'original fabric pattern')
        detail='continuous original fibers, subtle woven highlights and shadows' if knitted else 'continuous original material, original highlights and shadows'
        return f'{visual_style(prompt)}, {fabric}, {palette}, {pattern}, {detail}, unchanged garment design and silhouette, original natural folds'
    content=prompt.lower();surfaces=[]
    if caption_has(content,('city','cities','building','street','architecture','balcony','balconies','window frame')):
        surfaces.append('original building facades, varied window frames, coherent architectural reflections')
    if caption_has(content,('tree','leaf','leaves','foliage','blossom','garden','plant')):
        surfaces.append('original branches, overlapping leaves and blossom petals, natural foliage shadows')
    if caption_has(content,('room','indoor','wood','wooden','furniture')):
        surfaces.append('original furniture and wood grain, consistent room perspective')
    if caption_has(content,('sea','ocean','water','beach')):
        surfaces.append('original ripples and rocks, soft reflected daylight')
    if caption_has(content,('mountain','landscape','hill')):
        surfaces.append('original textured rocks and layered landscape contours')
    detail=', '.join(surfaces) if surfaces else 'existing scene surfaces and textures'
    return f'{visual_style(prompt)}, {detail}, original colors, unchanged scene layout and edges, subtle natural light and shadow'

def local_skin_prompt(prompt):
    # Captioned clothes/person descriptions must not turn a skin crop into fabric
    # or a new portrait. Actual face pixels are separately restored from source.
    return f'{visual_style(prompt)}, original skin surface and flesh tone, existing fine skin texture, coherent soft illumination, subtle natural highlights and shadows, unchanged anatomy and outline, original pose and subject identity'


def matching_palette(image, source, shadow_floor=0., source_shadows=True):
    """Use generated light/shadow while retaining the source's color components."""
    from PIL import Image
    import numpy as np
    from PIL import Image
    generated=image.convert('YCbCr').split()
    original=source.convert('YCbCr').split()
    luminance=generated[0]
    if shadow_floor>0:
        # Existing dark source details may remain dark. New shadows over a light
        # blue/pink/brown surface retain a midtone instead of becoming black.
        floor=min(.45,float(shadow_floor))*255
        minimum=np.minimum(np.asarray(original[0],dtype=np.float32),floor) if source_shadows else floor
        luminance=Image.fromarray(np.maximum(np.asarray(luminance),minimum).astype(np.uint8))
    # RGB clipping after a YCbCr merge changes the intended QR luminance,
    # especially when brightening saturated blue. Reduce chroma only as much
    # as the RGB gamut requires, keeping both the hue and target luminance.
    y=np.asarray(luminance,dtype=np.float32)
    cb=np.asarray(original[1],dtype=np.float32)-128
    cr=np.asarray(original[2],dtype=np.float32)-128
    offsets=np.stack((1.402*cr,-.344136*cb-.714136*cr,1.772*cb),axis=-1)
    limits=np.where(offsets>=0,(255-y[...,None])/np.maximum(offsets,1e-5),y[...,None]/np.maximum(-offsets,1e-5))
    saturation=np.clip(limits.min(axis=-1),0,1)
    rgb=y[...,None]+offsets*saturation[...,None]
    return Image.fromarray(np.clip(rgb,0,255).round().astype(np.uint8))


def matching_condition(condition, source, variants, rotation=0):
    """Choose among valid standard QR masks by source luminance, retaining payload."""
    import numpy as np
    from PIL import Image
    grid=np.asarray(condition)[:,:,0];yy,xx=np.where(grid!=128)
    left,top,right,bottom=int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1
    reference=np.asarray(source.convert('L'),dtype=np.float32)[top:bottom,left:right]/255
    selected=condition;best=float('inf');index=-1
    for number,path in enumerate(variants):
        with Image.open(path) as opened:
            option=opened.convert('RGB')
        array=np.asarray(option)[:,:,0];rows,cols=np.where(array!=128)
        if not len(rows):continue
        box=(int(cols.min()),int(rows.min()),int(cols.max())+1,int(rows.max())+1)
        if box[2]-box[0]!=right-left or box[3]-box[1]!=bottom-top:continue
        cropped=option.crop(box).rotate(rotation)
        code=np.asarray(cropped)[:,:,0]>128
        cost=np.where(code,np.maximum(.6-reference,0)**2,np.maximum(reference-.35,0)**2).mean()
        if cost<best:
            best=float(cost);index=number;selected=condition.copy()
            selected.paste(cropped,(left,top))
    if index>=0:emit('progress',message=f'Matching standard QR mask to original light/shadow / 按原图明暗匹配标准二维码掩码: {index}')
    return selected


def material_blueprint(condition, source, style='adaptive'):
    """Independent QAB-inspired guide: retain texture outside adaptive centers.

    This is only a ControlNet input, never a finished image or decoding target.
    """
    import math
    import numpy as np
    from PIL import Image
    grid=np.asarray(condition)[:,:,0];rows,cols=np.where(grid!=128)
    left,top,right,bottom=int(cols.min()),int(rows.min()),int(cols.max())+1,int(rows.max())+1
    side=right-left;module=0
    for line in grid[top:bottom,left:right]:
        for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,side]):module=math.gcd(module,int(run))
    if module<4:return condition
    guide=np.full(grid.shape,128,dtype=np.float32)
    original=np.asarray(source.convert('L'),dtype=np.float32)/255
    # Preserve source texture. A wider center is used only where it is necessary
    # to communicate the bit, rather than filling every cell with a square.
    for y in range(top,bottom,module):
        for x in range(left,right,module):
            patch=original[y:y+module,x:x+module].copy()
            ink=grid[y+module//2,x+module//2]<128
            threshold=.45 if ink else .65
            if style=='soft':
                yy,xx=np.mgrid[:module,:module]
                distance=((xx+.5)/module-.5)**2+((yy+.5)/module-.5)**2
                center=patch[module//3:module-module//3,module//3:module-module//3].mean()
                radius=.20+min(.15,abs(float(center)-threshold)*.3)
                influence=np.exp(-distance/(2*radius**2))
                # A soft material hint changes just the encoding sample. The
                # canonical grid remains the decoder target, never this guide.
                target=.28 if ink else .82
                corrected=np.minimum(patch,target) if ink else np.maximum(patch,target)
                guide[y:y+module,x:x+module]=(patch*(1-influence)+corrected*influence)*255
                continue
            if (ink and patch.mean()<=threshold) or (not ink and patch.mean()>=threshold):
                guide[y:y+module,x:x+module]=patch*255
                continue
            for width in range(max(3,module//3),module+1,2):
                candidate=patch.copy();start=(module-width)//2
                core=candidate[start:start+width,start:start+width]
                core[:]=np.minimum(core,.3) if ink else np.maximum(core,.8)
                if (ink and candidate.mean()<=threshold) or (not ink and candidate.mean()>=threshold):break
            guide[y:y+module,x:x+module]=candidate*255
    # Keep only the finder center cross in the guide. The surrounding texture
    # gives the model freedom to disguise the familiar concentric box.
    for x,y in finder_boxes(grid,left,top,side,module):
        yy,xx=np.mgrid[:7*module,:7*module]
        cross=(abs(xx+.5-3.5*module)<module*.5)|(abs(yy+.5-3.5*module)<module*.5)
        region=guide[y:y+7*module,x:x+7*module]
        region[:]=original[y:y+7*module,x:x+7*module]*255
        region[cross]=np.where(grid[y:y+7*module,x:x+7*module][cross]<128,40,215)
    # Finder separation is essential: a dark clothing motif touching the outer
    # bar changes the 1:1:3:1:1 runs measured by ordinary camera decoders.
    separators=finder_separators(grid.shape,left,top,side,module,finder_boxes(grid,left,top,side,module))
    guide[separators]=np.maximum(original[separators]*255,204)
    return Image.fromarray(np.clip(guide,0,255).astype(np.uint8)).convert('RGB')


def finder_boxes(grid,left,top,side,module):
    """Locate the three exact structural patterns in an oriented QR guide."""
    import numpy as np
    yy,xx=np.mgrid[:7,:7]
    expected=(xx==0)|(xx==6)|(yy==0)|(yy==6)|((xx>=2)&(xx<=4)&(yy>=2)&(yy<=4))
    found=[]
    for x,y in ((left,top),(left+side-7*module,top),(left+side-7*module,top+side-7*module),(left,top+side-7*module)):
        actual=grid[y+module//2:y+7*module:module,x+module//2:x+7*module:module]<128
        if actual.shape==(7,7) and np.array_equal(actual,expected):found.append((x,y))
    return found


def organic_condition(condition):
    """Connected rounded ControlNet hints; the standard matrix stays separate."""
    import math
    import numpy as np
    from PIL import Image,ImageDraw
    grid=np.asarray(condition)[:,:,0];yy,xx=np.where(grid!=128)
    if not len(xx):return condition
    left,top,right,bottom=int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1
    side=right-left;module=0
    for line in grid[top:bottom,left:right]:
        for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,side]):module=math.gcd(module,int(run))
    if module<4:return condition
    result=Image.new('RGB',condition.size,(128,128,128));draw=ImageDraw.Draw(result)
    draw.rectangle((left,top,right-1,bottom-1),fill='white')
    bits=grid[top+module//2:bottom:module,left+module//2:right:module]<128
    for row,col in np.argwhere(bits):
        x,y=left+(col+.5)*module,top+(row+.5)*module;radius=module*.38
        draw.ellipse((x-radius,y-radius,x+radius,y+radius),fill='black')
        if col+1<bits.shape[1] and bits[row,col+1]:draw.rectangle((x,y-module*.25,x+module,y+module*.25),fill='black')
        if row+1<bits.shape[0] and bits[row+1,col]:draw.rectangle((x-module*.25,y,x+module*.25,y+module),fill='black')
    array=np.asarray(result).copy()
    for x,y in finder_boxes(grid,left,top,side,module):
        ry,rx=np.mgrid[:7*module,:7*module];dx=rx+.5-3.5*module;dy=ry+.5-3.5*module
        cross=(abs(dx)<module*.5)|(abs(dy)<module*.5)
        patch=array[y:y+7*module,x:x+7*module]
        patch[:]=128;patch[cross]=grid[y:y+7*module,x:x+7*module][cross,None]
    return Image.fromarray(array)


def finder_separators(shape,left,top,side,module,boxes=None):
    """One light module around each finder; the rest of the texture is free."""
    import numpy as np
    mask=np.zeros(shape,dtype=bool)
    for x,y in boxes if boxes is not None else ((left,top),(left+side-7*module,top),(left,top+side-7*module)):
        x0=max(0,x-module);x1=min(shape[1],x+8*module)
        y0=max(0,y-module);y1=min(shape[0],y+8*module)
        mask[y0:y1,x0:x1]=True
        mask[y:y+7*module,x:x+7*module]=False
    return mask

def rounded_finders(condition):
    import math
    import numpy as np
    from PIL import ImageDraw
    grid=np.asarray(condition)[:,:,0];yy,xx=np.where(grid!=128)
    left,top,right,bottom=int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1
    module=0
    for line in grid[top:bottom,left:right]:
        for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,right-left]):module=math.gcd(module,int(run))
    result=condition.copy();draw=ImageDraw.Draw(result)
    for x,y in finder_boxes(grid,left,top,right-left,module):
        draw.rectangle((x,y,x+7*module-1,y+7*module-1),fill='white')
        for inset,color in ((0,'black'),(1,'white'),(2,'black')):
            draw.ellipse((x+inset*module,y+inset*module,x+(7-inset)*module-1,y+(7-inset)*module-1),fill=color)
    return result


def supports_scanning_guidance(scheduler):
    config = scheduler.config
    prediction = config.get('prediction_type') if hasattr(config, 'get') else getattr(config, 'prediction_type', None)
    # The luminance correction below converts a score into an epsilon residual.
    # v_prediction/sample need different conversions; retain normal ControlNet.
    return prediction == 'epsilon'


def scanning_scheduler(pipe, condition, reference, scale=700., edge=768, finder_shape='square', dark=.45, light=.65, locator_weight=.3, quiet_light=.55, preservation=2., start=850):
    """Prototype: DDIM luminance guidance with an image-preservation penalty.

    Derived from the SRPG equations, not the third-party pipeline source.
    Kept opt-in until visual/scanner regression measurements are sufficient.
    """
    if not supports_scanning_guidance(pipe.scheduler):
        return pipe.scheduler
    import math
    import numpy as np
    import torch
    import torch.nn.functional as F
    from diffusers import DDIMScheduler
    grid=np.asarray(condition)[:,:,0]
    yy,xx=np.where(grid!=128)
    left,top,right,bottom=int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1
    side=right-left;module=0
    for line in grid[top:bottom,left:right]:
        changes=np.flatnonzero(np.diff(line.astype(np.int16)))+1
        for run in np.diff(np.r_[0,changes,side]):module=math.gcd(module,int(run))
    if module<4:return pipe.scheduler
    emit('progress',message=f'Scanning guidance / 扫码引导: module={module}, scale={scale:g}')
    centers=np.arange(module/2,side,module)
    cy,cx=np.meshgrid(centers+top,centers+left,indexing='ij')
    coordinates=np.stack((2*cx/condition.width-1,2*cy/condition.height-1),-1)
    art_grid=np.asarray(rounded_finders(condition))[:,:,0] if finder_shape=='circle' else grid
    targets=(art_grid[cy.astype(int),cx.astype(int)]>128).astype(np.float32)
    quiet=np.zeros(grid.shape,dtype=np.float32)
    quiet[max(0,top-4*module):min(grid.shape[0],bottom+4*module),max(0,left-4*module):min(grid.shape[1],right+4*module)]=1
    quiet[top:bottom,left:right]=0
    functional=np.zeros(grid.shape,dtype=np.float32)
    boxes=finder_boxes(grid,left,top,side,module)
    for x,y in boxes:
        yy,xx=np.mgrid[:7*module,:7*module]
        dx=xx+.5-3.5*module;dy=yy+.5-3.5*module
        cross=(abs(dx)<module*.5)|(abs(dy)<module*.5)|(abs(dx-dy)<module*.35)|(abs(dx+dy)<module*.35)
        cross|=(abs(dx)<2.5*module)&(abs(dy)<2.5*module)
        functional[y:y+7*module,x:x+7*module]=cross
    separators=finder_separators(grid.shape,left,top,side,module,boxes)
    functional[separators]=1
    functional_targets=(art_grid>128).astype(np.float32)
    functional_targets[separators]=1
    for parameter in pipe.vae.parameters():parameter.requires_grad_(False)
    class GuidedDDIM(DDIMScheduler):
        def step(self, model_output, timestep, sample, **kwargs):
            first=dict(kwargs);first['return_dict']=True
            result=super().step(model_output,timestep,sample,**first)
            if int(timestep)>start:return result if kwargs.get('return_dict',True) else (result.prev_sample,)
            with torch.inference_mode(False),torch.enable_grad():
                alpha=self.alphas_cumprod[int(timestep)].to(sample.device).float()
                original=result.pred_original_sample.detach().clone().requires_grad_(True)
                reduced=F.interpolate(original,size=(min(edge//8,original.shape[-2]),min(edge//8,original.shape[-1])),mode='bilinear',align_corners=False)
                decoded=(pipe.vae.decode(reduced/pipe.vae.config.scaling_factor,return_dict=False)[0].float()+1)/2
                luminance=(decoded*decoded.new_tensor([.299,.587,.114])[None,:,None,None]).sum(1,keepdim=True)
                radius=max(3,int(module*decoded.shape[-1]/condition.width/3)//2*2+1)
                smooth=F.avg_pool2d(luminance,radius,stride=1,padding=radius//2)
                points=torch.tensor(coordinates,device=sample.device,dtype=torch.float32)[None]
                measured=F.grid_sample(smooth,points,align_corners=False)[0,0]
                target=torch.tensor(targets,device=sample.device)
                code_loss=(F.relu(measured-dark)*(1-target)+F.relu(light-measured)*target).mean()
                dense=torch.tensor(functional_targets,device=sample.device)[None,None]
                dense=F.interpolate(dense,size=luminance.shape[-2:],mode='nearest')
                locator=torch.tensor(functional,device=sample.device)[None,None]
                locator=F.interpolate(locator,size=luminance.shape[-2:],mode='nearest')
                locator_loss=((F.relu(luminance-.4)*(1-dense)+F.relu(.7-luminance)*dense)*locator).sum()/locator.sum().clamp_min(1)
                spacing=torch.tensor(quiet,device=sample.device)[None,None]
                spacing=F.interpolate(spacing,size=luminance.shape[-2:],mode='nearest')
                quiet_loss=(F.relu(quiet_light-luminance)*spacing).sum()/spacing.sum().clamp_min(1)
                ref=torch.tensor(np.asarray(reference.resize((decoded.shape[-1],decoded.shape[-2]))).copy(),device=sample.device,dtype=torch.float32).permute(2,0,1)[None]/255
                # Preserve garment silhouette/folds without freezing every yarn
                # highlight which must carry an encoded bit.
                structure=(F.avg_pool2d(decoded,17,stride=1,padding=8)-F.avg_pool2d(ref,17,stride=1,padding=8)).square().mean()
                loss=scale*(code_loss+locator_loss*locator_weight+quiet_loss*.1)+preservation*structure
                derivative=torch.autograd.grad(loss,original)[0]
                emit('guidance',loss=float(code_loss.detach()),gradient=float(derivative.detach().abs().mean()),timestep=int(timestep))
                score=derivative/alpha.sqrt().clamp_min(.03)
                guided=model_output.detach().clone()+((1-alpha).sqrt()*score).to(model_output.dtype)
            return super().step(guided,timestep,sample,**kwargs)
    return GuidedDDIM.from_config(pipe.scheduler.config)

def refine_module_light(image, condition, materials=None):
    """Adjust failing module centers with soft tonal spots, retaining texture and skin."""
    import math
    import numpy as np
    from PIL import Image
    grid=np.asarray(condition)[:,:,0]
    yy,xx=np.where(grid!=128)
    left,top,right,bottom=int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1
    side=right-left
    scale=0
    for line in grid[top:bottom,left:right]:
        changes=np.flatnonzero(np.diff(line.astype(np.int16)))+1
        for run in np.diff(np.r_[0,changes,side]):scale=math.gcd(scale,int(run))
    if scale<4:return
    allowed=np.ones(grid.shape,dtype=bool)
    if materials is not None:
        allowed=~np.isin(np.asarray(materials.resize(image.size,Image.Resampling.NEAREST)),(2,3,11,12,13,14,15))
    original=np.asarray(image,dtype=np.float32)/255
    count=side//scale
    yy,xx=np.mgrid[:scale,:scale]
    distance=((xx+.5)/scale-.5)**2+((yy+.5)/scale-.5)**2
    for radius in (.18,.23,.28,.34):
        result=original.copy()
        influence=np.exp(-distance/(2*radius**2))[...,None]
        for row in range(count):
            for col in range(count):
                y,x=top+row*scale,left+col*scale
                area=result[y:y+scale,x:x+scale]
                luminance=(area*np.array([.299,.587,.114])).sum(axis=2,keepdims=True)
                ink=grid[y+scale//2,x+scale//2]<128
                center=luminance[scale//3:scale-scale//3,scale//3:scale-scale//3].mean()
                if (ink and center<.38) or (not ink and center>.68):continue
                functional=(col<8 and row<8) or (col>=count-8 and row<8) or (col<8 and row>=count-8) or col==6 or row==6
                amount=(np.ones_like(influence) if functional else influence)*allowed[y:y+scale,x:x+scale,None]
                target=.27 if ink else .76
                corrected=area*np.minimum(1,target/np.maximum(luminance,.001)) if ink else area+(1-area)*np.maximum(0,(target-luminance)/np.maximum(1-luminance,.001))
                result[y:y+scale,x:x+scale]=area*(1-amount)+corrected*amount
        yield Image.fromarray(np.clip(result*255,0,255).astype(np.uint8)),radius


def refine_latent(pipe,image,condition,steps=80,**settings):
    import torch
    original_dtype=pipe.vae.dtype
    # Generation normally runs in inference mode. Parameter conversions made
    # there produce inference tensors, which cannot participate in VAE gradients.
    with torch.inference_mode(False),torch.enable_grad():
        try:
            return _refine_latent(pipe,image,condition,steps,**settings)
        finally:
            pipe.vae.to(dtype=original_dtype)


def latent_refinement_divisor(condition,edge=512):
    """Choose a memory-bounded reduction that preserves the original QR grid."""
    import math
    import numpy as np
    grid=np.asarray(condition)[:,:,0];yy,xx=np.where(grid!=128)
    if not len(xx) or edge<8:return None
    left,top,right,bottom=int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1
    side=right-left;module=0
    if bottom-top!=side:return None
    # Measure before resizing: an indivisible reduction can turn a 16px grid
    # into alternating 5/6px cells whose GCD falsely reports a 1px module.
    for line in grid[top:bottom,left:right]:
        for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,side]):module=math.gcd(module,int(run))
    minimum=max(1,int(math.ceil(max(condition.size)/edge)))
    for divisor in range(minimum,module+1):
        if any(value%divisor for value in (module,condition.width,condition.height,left,top,right,bottom)):continue
        # The VAE downsamples by eight. Non-integral latent sizes would change
        # the decoded image dimensions relative to the QR sampling coordinates.
        if condition.width//divisor%8 or condition.height//divisor%8:continue
        return divisor
    return None


def _refine_latent(pipe,image,condition,steps=80,dark=.45,light=.65,structure_weight=.5,texture_weight=.08,edge=512,locator_structure='stable',locator_dark=.4,locator_light=.7,locator_weight=.3,locator_background=True):
    """Optimize VAE material features around encoding samples, retaining the scene.

    Independent prototype of center constraints plus image harmony; not a
    reproduction of a paper or a direct overlay of QR pixels.
    """
    import math
    import numpy as np
    import torch
    import torch.nn.functional as F
    from PIL import Image
    if locator_structure not in ('stable','organic','dots','alignment'):
        raise ValueError('Unsupported locator refinement structure')
    output_size=image.size
    # The VAE gradient must be computed in fp32. Optimize at an integer reduction
    # so QR cell boundaries remain exact, and keep the complete patch on return.
    divisor=latent_refinement_divisor(condition,edge) if image.size==condition.size else None
    if divisor is None:
        emit('progress',message='Skipping unaligned latent refinement / 码区无法安全等比缩整，跳过潜变量修整')
        return image
    if divisor>1:
        size=(image.width//divisor,image.height//divisor)
        image=image.resize(size,Image.Resampling.LANCZOS)
        condition=condition.resize(size,Image.Resampling.NEAREST)
    grid=np.asarray(condition)[:,:,0];yy,xx=np.where(grid!=128)
    left,top,right,bottom=int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1
    module=0;side=right-left
    for line in grid[top:bottom,left:right]:
        for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,side]):module=math.gcd(module,int(run))
    centers=np.arange(module/2,side,module);cy,cx=np.meshgrid(centers+top,centers+left,indexing='ij')
    coordinates=np.stack((2*cx/image.width-1,2*cy/image.height-1),-1)
    targets=(grid[cy.astype(int),cx.astype(int)]>128).astype(np.float32)
    support=np.zeros(grid.shape,dtype=np.float32);boxes=finder_boxes(grid,left,top,side,module) if locator_structure!='alignment' else []
    data_weight=np.ones(targets.shape,dtype=np.float32)
    bits=(grid>128).astype(np.float32)
    if locator_structure=='alignment':
        # A small alignment target carries geometry, not image content. Preserve
        # its horizontal/vertical 1:1:1 sampling with material around the axes.
        ay,ax=np.mgrid[:side,:side]
        support[top:bottom,left:right]=(abs(ax+.5-side/2)<module*.5)|(abs(ay+.5-side/2)<module*.5)
        data_weight[:]=0
    for x,y in boxes:
        ry,rx=np.mgrid[:7*module,:7*module];dx=rx+.5-3.5*module;dy=ry+.5-3.5*module
        diagonal=module*(.5 if locator_structure=='organic' else .35)
        axes=(abs(dx)<module*.5)|(abs(dy)<module*.5)|(abs(dx-dy)<diagonal)|(abs(dx+dy)<diagonal)
        if locator_structure=='stable':axes|=(abs(dx)<2.5*module)&(abs(dy)<2.5*module)
        else:
            # A locator is detected along scanning axes, not all 49 cell centers.
            # Leave the off-axis material free instead of forcing a solid ring.
            row,col=(y-top)//module,(x-left)//module
            data_weight[row:row+7,col:col+7]=0
            # Concentric-pattern decoders also need a closed light path and
            # a solid central core. Keep those, freeing the outer corners so
            # source material can connect across the locator's silhouette.
            radius=(abs(dx/module)**8+abs(dy/module)**8)**(1/8)
            ring=(radius>=1.5)&(radius<=2.5)
            core=(abs(dx)<1.5*module)&(abs(dy)<1.5*module)
            if locator_structure=='dots':
                radius=(dx*dx+dy*dy)**.5/module
                ring=(radius>=1.5)&(radius<=2.5)
                core=radius<1.5
                dots=np.zeros(core.shape,dtype=bool)
                for angle in np.arange(8)*np.pi/4:
                    px=3*module*np.cos(angle);py=3*module*np.sin(angle)
                    dots|=(dx-px)**2+(dy-py)**2<(.4*module)**2
                # The outer frame is replaced by isolated material samples.
                # Its intervening pixels stay locally light enough to avoid
                # merging those samples into a false finder boundary.
                axes=np.ones(core.shape,dtype=bool) if locator_background else ring|core|dots
                bits[y:y+7*module,x:x+7*module]=1
                bits[y:y+7*module,x:x+7*module][dots]=0
            axes|=ring|core
            bits[y:y+7*module,x:x+7*module][ring]=1
            bits[y:y+7*module,x:x+7*module][core]=0
        support[y:y+7*module,x:x+7*module]=axes
    separators=finder_separators(grid.shape,left,top,side,module,boxes);support[separators]=1
    bits[separators]=1
    device=pipe.vae.device
    pipe.vae.to(dtype=torch.float32);dtype=torch.float32
    for parameter in pipe.vae.parameters():parameter.requires_grad_(False)
    with torch.inference_mode(False),torch.enable_grad():
        reference=torch.tensor(np.asarray(image).copy(),device=device,dtype=torch.float32).permute(2,0,1)[None]/255
        with torch.no_grad():initial=pipe.vae.encode((reference*2-1).to(dtype)).latent_dist.mode()*pipe.vae.config.scaling_factor
        latent=initial.float().detach().clone().requires_grad_(True)
        optimizer=torch.optim.Adam([latent],lr=.06)
        points=torch.tensor(coordinates,device=device,dtype=torch.float32)[None]
        target=torch.tensor(targets,device=device)
        sampling_weight=torch.tensor(data_weight,device=device)
        marker=torch.tensor(support,device=device)[None,None]
        dense=torch.tensor(bits,device=device)[None,None]
        dark_field=torch.as_tensor(locator_dark,device=device,dtype=torch.float32)
        light_field=torch.as_tensor(locator_light,device=device,dtype=torch.float32)
        if dark_field.ndim==2:
            dark_field=F.interpolate(dark_field[None,None],size=grid.shape,mode='bilinear',align_corners=False)
        if light_field.ndim==2:
            light_field=F.interpolate(light_field[None,None],size=grid.shape,mode='bilinear',align_corners=False)
        radius=max(3,module//3//2*2+1)
        for index in range(max(1,min(160,int(steps)))):
            optimizer.zero_grad(set_to_none=True)
            decoded=(pipe.vae.decode((latent/pipe.vae.config.scaling_factor).to(dtype),return_dict=False)[0].float()+1)/2
            lum=(decoded*decoded.new_tensor([.299,.587,.114])[None,:,None,None]).sum(1,keepdim=True)
            measured=F.grid_sample(F.avg_pool2d(lum,radius,stride=1,padding=radius//2),points,align_corners=False)[0,0]
            code=((F.relu(measured-dark)*(1-target)+F.relu(light-measured)*target)*sampling_weight).sum()/sampling_weight.sum().clamp_min(1)
            locator=((F.relu(lum-dark_field)*(1-dense)+F.relu(light_field-lum)*dense)*marker).sum()/marker.sum().clamp_min(1)
            # Low frequency structure carries the scene/garment layout. A small
            # full color term limits changes to existing high frequency texture.
            structure=(F.avg_pool2d(decoded,33,stride=1,padding=16)-F.avg_pool2d(reference,33,stride=1,padding=16)).square().mean()
            texture=(decoded-reference).square().mean()
            loss=code+locator*locator_weight+structure*structure_weight+texture*texture_weight
            # Scale the backward pass to avoid fp16 VAE gradient underflow;
            # Adam normalizes this scale when calculating its parameter step.
            (loss*4096).backward();optimizer.step()
            if index%8==0 or index==steps-1:emit('progress',message=f'Latent material refinement / 材质潜变量修整 {index+1}/{steps}, centers={float(code.detach()):.5f}, grad={float(latent.grad.detach().abs().mean()):.5g}')
            if index>=24 and float(code.detach())<.0005 and float(locator.detach())<.002:break
        with torch.no_grad():result=(pipe.vae.decode((latent/pipe.vae.config.scaling_factor).to(dtype),return_dict=False)[0].float()+1)/2
        array=result[0].permute(1,2,0).clamp(0,1).cpu().numpy()
        return Image.fromarray((array*255).round().astype(np.uint8)).resize(output_size,Image.Resampling.LANCZOS)


# Local threshold calculations adapted from the ZXing / ZXing-C++
# HybridBinarizer algorithms (Apache-2.0); see THIRD_PARTY_NOTICES.md.
def _rgb(image):
    import numpy as np
    from PIL import Image
    if isinstance(image,Image.Image):return np.asarray(image.convert('RGB'))
    values=np.asarray(image)
    if values.ndim!=3 or values.shape[2]!=3:raise ValueError('Expected RGB image')
    if values.dtype!=np.uint8:raise ValueError('RGB arrays must use uint8 values')
    return values


def luminance_fields(image):
    """Return exact integer RGB projections used by CPP and Java file readers."""
    import numpy as np
    from PIL import Image
    rgb=_rgb(image).astype(np.uint32)
    return {
        'bt601_gray':((306*rgb[:,:,0]+601*rgb[:,:,1]+117*rgb[:,:,2]+512)>>10).astype(np.uint8),
        'java_gray':((rgb[:,:,0]+2*rgb[:,:,1]+rgb[:,:,2])//4).astype(np.uint8),
    }


def _blocks(gray):
    import numpy as np
    from PIL import Image
    gray=np.asarray(gray)
    if gray.ndim!=2 or gray.dtype!=np.uint8:raise ValueError('Expected uint8 grayscale')
    height,width=gray.shape
    if min(height,width)<40:
        raise ValueError('Local Hybrid fields require both dimensions >=40; smaller images use global histograms')
    ys=np.minimum(np.arange((height+7)//8)*8,height-8)
    xs=np.minimum(np.arange((width+7)//8)*8,width-8)
    values=gray[(ys[:,None,None,None]+np.arange(8)[None,None,:,None]),
                (xs[None,:,None,None]+np.arange(8)[None,None,None,:])]
    return values.min((2,3)).astype(np.int32),values.max((2,3)).astype(np.int32),values.sum((2,3))//64,ys,xs


def _window_sum(values):
    import numpy as np
    from PIL import Image
    height,width=values.shape
    cy=np.clip(np.arange(height),2,height-3);cx=np.clip(np.arange(width),2,width-3)
    result=np.zeros(values.shape,dtype=np.int32)
    for dy in range(-2,3):
        for dx in range(-2,3):result+=values[(cy+dy)[:,None],(cx+dx)[None,:]]
    return result


def _expand(values,shape,ys,xs):
    import numpy as np
    from PIL import Image
    # The final partial blocks overlap their preceding blocks, as in both
    # authors' implementations. Later rows/columns overwrite those pixels.
    result=np.zeros(shape,dtype=np.uint8)
    for y,offset_y in enumerate(ys):
        for x,offset_x in enumerate(xs):result[offset_y:offset_y+8,offset_x:offset_x+8]=values[y,x]
    return result


def cpp_threshold_map(gray):
    """ZXing-C++ 2.3.0 USE_NEW_ALGORITHM, including flat-order gap filling."""
    import numpy as np
    from PIL import Image
    low,high,_,ys,xs=_blocks(gray)
    values=np.where(high-low>24,(high+low)//2,0).astype(np.int32)
    sums=values*2+_window_sum(values)
    counts=(values>0).astype(np.int32)*2+_window_sum((values>0).astype(np.int32))
    thresholds=np.floor_divide(sums,counts,out=np.zeros_like(sums),where=counts>0)
    flat=thresholds.ravel();valid=np.flatnonzero(flat)
    if len(valid):
        previous=-1
        for index in valid:
            if index>previous+1:flat[previous+1:index]=flat[index]
            previous=index
        flat[previous+1:]=flat[previous]
    return _expand(thresholds,np.asarray(gray).shape,ys,xs)


def java_threshold_map(gray):
    """ZXing Java 3.5.4, including low-range min/2 and neighbor correction."""
    import numpy as np
    from PIL import Image
    low,high,means,ys,xs=_blocks(gray)
    points=means.astype(np.int32)
    for y in range(points.shape[0]):
        for x in range(points.shape[1]):
            if high[y,x]-low[y,x]<=24:
                points[y,x]=low[y,x]//2
                if x and y:
                    neighbor=(points[y-1,x]+2*points[y,x-1]+points[y-1,x-1])//4
                    if low[y,x]<neighbor:points[y,x]=neighbor
    return _expand(_window_sum(points)//25,np.asarray(gray).shape,ys,xs)


def threshold_fields(image):
    """Return full-sized uint8 gray and local threshold fields for both readers."""
    import numpy as np
    from PIL import Image
    fields=luminance_fields(image)
    fields['cpp_threshold']=cpp_threshold_map(fields['bt601_gray'])
    fields['java_threshold']=java_threshold_map(fields['java_gray'])
    return fields


def _project(field,size):
    import numpy as np
    from PIL import Image
    return np.asarray(Image.fromarray(field.astype(np.float32)).resize(size,Image.Resampling.NEAREST)).copy()


def projected_targets(image,margin=8,thumbnail_edge=800):
    """Map original+thumbnail constraints to a full-canvas BT.601 target.

    dark_max/light_min are normalized float32 arrays suitable for masked hinge
    losses. They do not contain a finder mask: apply the existing structural
    support separately. The Java projection's chroma offset is subtracted before
    mapping its limits into BT.601 space. Holding chroma fixed keeps this offset
    fixed until RGB channels clip, which feasible_* identifies separately.
    """
    import numpy as np
    from PIL import Image
    if not 0<=float(margin)<128:raise ValueError('Invalid gray-level margin')
    if thumbnail_edge<40:raise ValueError('Thumbnail edge must be >=40')
    image=Image.fromarray(_rgb(image));size=image.size
    views=[('full',image)]
    thumbnail=image.copy();thumbnail.thumbnail((thumbnail_edge,thumbnail_edge),Image.Resampling.LANCZOS)
    if thumbnail.size!=size:views.append(('thumbnail',thumbnail))
    dark=np.full((image.height,image.width),np.inf,dtype=np.float32)
    light=np.full(dark.shape,-np.inf,dtype=np.float32)
    diagnostics=[]
    for name,view in views:
        fields=threshold_fields(view)
        offset=fields['java_gray'].astype(np.float32)-fields['bt601_gray'].astype(np.float32)
        cpp=fields['cpp_threshold'].astype(np.float32)
        java=fields['java_threshold'].astype(np.float32)-offset
        dark_view=np.minimum(cpp,java)-margin
        light_view=np.maximum(cpp,java)+margin
        dark=np.minimum(dark,_project(dark_view,size));light=np.maximum(light,_project(light_view,size))
        diagnostics.append(dict(name=name,size=view.size,**fields,dark_max=dark_view,light_min=light_view))
    rgb=np.asarray(image).astype(np.float32)
    original_gray=luminance_fields(image)['bt601_gray'].astype(np.float32)
    gamut_min=original_gray-rgb.min(2);gamut_max=original_gray+255-rgb.max(2)
    # Preserve raw infeasibility instead of silently declaring clipped colors safe.
    return {
        'dark_max':np.clip(dark/255,0,1).astype(np.float32),
        'light_min':np.clip(light/255,0,1).astype(np.float32),
        'dark_raw':dark,'light_raw':light,
        'feasible_dark':dark>=gamut_min,'feasible_light':light<=gamut_max,
        'gamut_min':gamut_min/255,'gamut_max':gamut_max/255,
        'margin':float(margin),'views':diagnostics,
    }


def refine_locator_points(pipe,image,condition,source,protected=None,margin=20,steps=100):
    """Replace continuous locator frames through native-resolution VAE inpainting.

    Preserve source chroma and change only each finder plus its separator. The
    surrounding eight samples retain general QR detection rather than requiring
    a proprietary circular-code reader. Final exact decoding is still required.
    """
    import math
    import numpy as np
    from PIL import Image,ImageDraw,ImageFilter
    if image.size!=condition.size or image.size!=source.size:
        raise ValueError('Locator refinement canvas sizes differ')
    grid=np.asarray(condition)[:,:,0];yy,xx=np.where(grid!=128)
    if not len(xx):return image
    left,top=int(xx.min()),int(yy.min());side=int(xx.max()-xx.min()+1)
    module=0
    for line in grid[top:top+side,left:left+side]:
        for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,side]):module=math.gcd(module,int(run))
    edge=min(512,min(image.size)//8*8)
    if module<8 or 9*module>edge:
        emit('progress',message='Locator samples too small for safe local refinement / 定位点像素不足，保留已生成的识别结构')
        return image
    working=image.copy()
    samples=grid[top+module//2:top+side:module,left+module//2:left+side:module]
    stencil=np.zeros((5,5),dtype=np.uint8);stencil[1:4,1:4]=255;stencil[2,2]=0
    alignments=[]
    for row in range(samples.shape[0]-4):
        for col in range(samples.shape[1]-4):
            if np.array_equal(samples[row:row+5,col:col+5],stencil):
                alignments.append((left+col*module,top+row*module))
    locations=[(x,y,7,'dots') for x,y in finder_boxes(grid,left,top,side,module)]
    locations += [(x,y,5,'alignment') for x,y in alignments]
    for number,(x,y,count,structure) in enumerate(locations):
        cx=max(0,min(working.width-edge,x+7*module//2-edge//2))
        cy=max(0,min(working.height-edge,y+7*module//2-edge//2))
        box=(cx,cy,cx+edge,cy+edge)
        local=Image.new('RGB',(edge,edge),(128,128,128))
        local.paste(condition.crop((x,y,x+count*module,y+count*module)),(x-cx,y-cy))
        fields=projected_targets(working,margin=margin)
        emit('progress',message=f'Refining material recognition points / 局部生成材质识别点 {number+1}/{len(locations)}')
        refined=refine_latent(pipe,working.crop(box),local,steps,edge=edge,
            dark=.46,light=.62,structure_weight=2.,texture_weight=.4,
            locator_structure=structure,locator_dark=fields['dark_max'][cy:cy+edge,cx:cx+edge],
            locator_light=fields['light_min'][cy:cy+edge,cx:cx+edge])
        refined=matching_palette(refined,source.crop(box),.38)
        mask=Image.new('L',(edge,edge))
        border=module if structure=='dots' else 0
        ImageDraw.Draw(mask).rectangle((x-cx-border,y-cy-border,x-cx+(count*module)+border-1,y-cy+(count*module)+border-1),fill=255)
        working.paste(refined,box[:2],mask.filter(ImageFilter.GaussianBlur(1)))
        if protected is not None:working=Image.composite(source,working,protected)
    return working


def independent_validation(image,expected):
    """Require exact QR payload at original and common sharing dimensions."""
    import zxingcpp
    from PIL import Image
    thumbnail=image.copy();thumbnail.thumbnail((800,800),Image.Resampling.LANCZOS)
    return all(any(value.text==expected for value in zxingcpp.read_barcodes(view,formats=zxingcpp.BarcodeFormat.QRCode)) for view in (image,thumbnail))


def control_payload(condition,supplied=None):
    """Bind an imported text payload to its control image without rewriting it."""
    import zxingcpp
    codes=zxingcpp.read_barcodes(condition,formats=zxingcpp.BarcodeFormat.QRCode)
    if len(codes)!=1:
        raise ValueError('Invalid QR control template / 二维码控制模板无法解码')
    decoded=codes[0].text
    if supplied is not None:
        if not isinstance(supplied,str) or not supplied:
            raise ValueError('Invalid imported QR content / 导入的二维码内容无效')
        if decoded!=supplied:
            # Do not include personal contact links or content in diagnostics.
            raise ValueError('QR control content mismatch / 二维码控制模板与导入内容不一致')
        return supplied
    return decoded


def transparent_locator_background(image,condition,source,protected=None,margin=8):
    """Recover source texture around generated points using minimal tonal edits.

    The surrounding source pixels pass through outside a circular recognition
    footprint. Inside it, only pixels below a reader's local light threshold are
    lifted. No flat fill, blurred backing plate or new black ink is introduced.
    The caller must validate the completed QR before accepting this variant.
    """
    import math
    import numpy as np
    from PIL import Image
    if image.size!=source.size or image.size!=condition.size:
        raise ValueError('Locator background canvas sizes differ')
    grid=np.asarray(condition)[:,:,0];yy,xx=np.where(grid!=128)
    if not len(xx):return image
    left,top=int(xx.min()),int(yy.min());side=int(xx.max()-xx.min()+1);module=0
    for line in grid[top:top+side,left:left+side]:
        for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,side]):
            module=math.gcd(module,int(run))
    if module<8:return image
    result=image.copy();dark=np.zeros(grid.shape,bool);light=np.zeros(grid.shape,bool)
    for x,y in finder_boxes(grid,left,top,side,module):
        bounds=(x-module,y-module,x+8*module,y+8*module)
        if bounds[0]<0 or bounds[1]<0 or bounds[2]>image.width or bounds[3]>image.height:
            return image
        gy,gx=np.mgrid[:9*module,:9*module]
        dx=(gx+.5)/module-4.5;dy=(gy+.5)/module-4.5;radius=np.hypot(dx,dy)
        points=radius<1.5
        for angle in np.arange(8)*np.pi/4:
            points|=(dx-3*np.cos(angle))**2+(dy-3*np.sin(angle))**2<.4**2
        original=np.asarray(source.crop(bounds)).copy();generated=np.asarray(image.crop(bounds))
        original[points]=generated[points]
        result.paste(Image.fromarray(original),bounds[:2])
        dark[bounds[1]:bounds[3],bounds[0]:bounds[2]]=points
        light[bounds[1]:bounds[3],bounds[0]:bounds[2]]=(radius<4)&~points
    edit=Image.fromarray((light|dark).astype(np.uint8)*255)
    for _ in range(3):
        fields=projected_targets(result,margin=margin)
        gray=np.asarray(result.convert('L')).astype(np.float32)/255
        gray[light]=np.maximum(gray[light],fields['light_min'][light])
        gray[dark]=np.minimum(gray[dark],fields['dark_max'][dark])
        toned=matching_palette(Image.fromarray(np.clip(gray*255,0,255).astype(np.uint8)).convert('RGB'),source,.38)
        result=Image.composite(toned,result,edit)
        if protected is not None:result=Image.composite(source,result,protected)
    return result


def restore_fabric_detail(image, source, labels, protected=None):
    """Recover fine source weave on edited clothing; callers must recheck decoding."""
    import numpy as np
    from PIL import Image, ImageFilter
    if image.size != source.size or image.size != labels.size or (protected is not None and image.size != protected.size):
        raise ValueError('Fabric detail canvases must have matching dimensions')
    generated = np.asarray(image.convert('RGB'))
    original = np.asarray(source.convert('RGB'))
    garment = np.isin(np.asarray(labels), (4, 7, 17)) & np.any(generated != original, axis=2)
    if protected is not None:
        guard = protected.convert('L').filter(ImageFilter.MaxFilter(11))
        garment &= np.asarray(guard) == 0
    if not garment.any():
        return image.copy()
    source_y = source.convert('YCbCr').split()[0]
    detail = np.asarray(source_y, dtype=np.float32) - np.asarray(source_y.filter(ImageFilter.GaussianBlur(2)), dtype=np.float32)
    y, cb, cr = image.convert('YCbCr').split()
    values = np.asarray(y, dtype=np.float32).copy()
    values[garment] += 0.5 * detail[garment]
    recovered = Image.merge('YCbCr', (Image.fromarray(np.clip(values, 0, 255).round().astype(np.uint8)), cb, cr)).convert('RGB')
    pixels = np.asarray(recovered).copy()
    pixels[~garment] = generated[~garment]
    if protected is not None:
        pixels[np.asarray(protected.convert('L')) > 0] = original[np.asarray(protected.convert('L')) > 0]
    return Image.fromarray(pixels)


GENERATION_PROFILE_FAMILIES = {
    'sd15-standard': 'sd15', 'dreamshaper-8': 'sd15',
    'sdxl-standard': 'sdxl', 'sdxl-base-1.0': 'sdxl',
}
SDXL_SURFACE_TEMPLATE = ('{prompt}. Preserve the source subject, face, pose, clothing design, '
                         'colors and composition. Blend the encoded light and shade into the existing surface texture.')


def generation_profile(request, family):
    """Model-specific wrapping and optional overrides, independent of placement.

    Zero means keep the existing phase's automatic value. The SDXL wording is
    a conservative text adaptation, not a claim of verified model-wide quality.
    Standard SD15 and DreamShaper retain their existing prompts and parameters.
    """
    import math
    configured = request.get('generation_profile')
    if configured is None:
        return None
    if not isinstance(configured, dict):
        raise ValueError('Invalid generation profile / 生图方案无效')
    identifier = configured.get('id') or family + '-standard'
    if not isinstance(identifier, str):
        raise ValueError('Invalid generation profile / 生图方案无效')
    if identifier != 'custom' and GENERATION_PROFILE_FAMILIES.get(identifier) != family:
        raise ValueError('Generation profile does not match the model family / 生图方案与模型类型不匹配')
    default_prompt = SDXL_SURFACE_TEMPLATE if family == 'sdxl' and identifier != 'custom' else '{prompt}'
    result = {'id': identifier}
    for key, token, default in (('prompt_template', '{prompt}', default_prompt),
                                ('negative_template', '{negative}', '{negative}')):
        configured_template = configured.get(key)
        template = default if configured_template is None or configured_template == '' else configured_template
        if (not isinstance(template, str) or len(template) > 4096 or token not in template
                or '{' in template.replace(token, '') or '}' in template.replace(token, '')):
            raise ValueError('Generation template must preserve ' + token + ' / 生图模板必须保留 ' + token)
        result[key] = template
    for key, low, high, integral in (('steps', 8, 60, True), ('guidance', 1, 12, False),
                                    ('strength', .15, .85, False), ('clip_skip', 0, 2, True)):
        value = configured.get(key, 0)
        if (isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value)
                or (value != 0 and not low <= value <= high) or (integral and value != int(value))):
            raise ValueError('Invalid generation profile parameter: ' + key + ' / 生图方案参数无效: ' + key)
        result[key] = int(value) if integral else float(value)
    if family != 'sd15' and result['clip_skip']:
        raise ValueError('CLIP skip is supported only for SD1.5 profiles / 仅 SD1.5 方案支持 CLIP skip')
    return result


def profile_arguments(arguments, profile, auto_prompt=True, skin_pass=False):
    if profile is None:
        return arguments
    result = dict(arguments)
    if auto_prompt:
        result['prompt'] = profile['prompt_template'].replace('{prompt}', result['prompt'])
        result['negative_prompt'] = profile['negative_template'].replace('{negative}', result.get('negative_prompt', ''))
    for field, parameter in (('steps', 'num_inference_steps'), ('guidance', 'guidance_scale'), ('clip_skip', 'clip_skip')):
        if profile[field]:
            result[parameter] = profile[field]
    if profile['strength'] and 'strength' in result:
        result['strength'] = min(.28, profile['strength']) if skin_pass else profile['strength']
    return result


def generate(request):
    environment = inference_environment(request)
    profile = generation_profile(request, request.get('family', 'sd15'))
    os.environ.update(HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1', HF_HUB_DISABLE_TELEMETRY='1', DO_NOT_TRACK='1')
    import torch
    import numpy as np
    from PIL import Image, ImageOps
    def fit_canvas(image,size,method):
        # Letterbox rounding differences instead of cropping edge subjects.
        fitted=ImageOps.contain(image,size,method=method)
        left=(size[0]-fitted.width)//2;top=(size[1]-fitted.height)//2
        padding=((top,size[1]-fitted.height-top),(left,size[0]-fitted.width-left))
        if image.mode=='RGB':padding+=((0,0),)
        return Image.fromarray(np.pad(np.asarray(fitted),padding,mode='edge' if image.mode=='RGB' else 'constant'))
    from diffusers import ControlNetModel, StableDiffusionControlNetPipeline, StableDiffusionControlNetImg2ImgPipeline, StableDiffusionXLControlNetPipeline, StableDiffusionXLControlNetImg2ImgPipeline, DPMSolverMultistepScheduler
    def infer(pipeline, arguments, skin_pass=False):
        return pipeline(**profile_arguments(arguments, profile, request.get('auto_prompt', True), skin_pass))
    base, control = Path(request['base']).resolve(), Path(request['control']).resolve()
    family = request.get('family', 'sd15')
    config = Path(request.get('config') or base.parent.parent / 'configs' / family)
    gpu = environment == 'cuda' and torch.cuda.is_available()
    free, total = torch.cuda.mem_get_info() if gpu else (0, 0)
    plan = policy(total / 2**30, free / 2**30, family, request.get('policy', 'auto'))
    emit('hardware', device=torch.cuda.get_device_name(0) if gpu else 'CPU', **plan)
    device = 'cuda' if gpu else 'cpu'
    source = Image.open(request['input']).convert('RGB') if request.get('input') else None
    condition = Image.open(request['condition']).convert('RGB')
    expected=None
    if request.get('verify_decoder',False):
        expected=control_payload(condition,request.get('expected'))
    materials = parse_materials(fit_canvas(source,condition.size,Image.Resampling.LANCZOS), request['models'], device) if source else None
    if source:
        fitted=fit_canvas(source,condition.size,Image.Resampling.LANCZOS)
        faces=face_guards(fitted,request['models'])
        import numpy as np
        values=np.asarray(materials).copy();values[np.asarray(faces)>0]=3
        materials=Image.fromarray(values)
        protected_pixelation=fit_canvas(pixelation_mask(source),condition.size,Image.Resampling.NEAREST)
        if protected_pixelation.getbbox():
            import numpy as np
            values=np.asarray(materials).copy();values[np.asarray(protected_pixelation)>0]=3
            materials=Image.fromarray(values)
            emit('progress',message='Protecting existing pixelated blocks / 保留原图已有马赛克区域')
    qr_region=None
    if materials is not None and request.get('auto_place',True):
        condition,materials,qr_region=adaptive_placement(condition,materials,source)
        emit('placement',**qr_region)
        emit('progress',message='Adjusted placement to protect the subject / 自动调整码区，避开皮肤和头发')
    original_canvas=fit_canvas(source,condition.size,Image.Resampling.LANCZOS) if source else None
    material_canvas=materials.copy() if materials is not None else None
    protected_canvas=Image.fromarray(np.isin(np.asarray(materials),(2,3,11,12,13,14,15)).astype(np.uint8)*255) if materials is not None else None
    if source:
        variants=sorted(Path(request['condition']).parent.glob('condition-mask[0-7].png'))
        if variants:condition=matching_condition(condition,original_canvas,variants,qr_region.get('rotation',0) if qr_region else 0)
    manual_skin=bool(request.get('manual_skin',False)) and not request.get('auto_place',True)
    skin_canvas=manual_skin_mask(material_canvas,condition) if manual_skin and material_canvas is not None else None
    if skin_canvas is not None:
        protected_canvas=Image.fromarray(np.where(np.asarray(skin_canvas)>0,0,np.asarray(protected_canvas)).astype(np.uint8))
        if skin_canvas.getbbox():
            emit('progress',message='Blending light into the selected skin area while retaining identity / 在手动皮肤区域融合原肤色明暗，保留五官和轮廓')
    if source and request.get('diagnostics'):
        diagnostic=Path(request['diagnostics']);diagnostic.mkdir(parents=True,exist_ok=True)
        original_canvas.save(diagnostic/'source-canvas.png')
        materials.save(diagnostic/'source-labels.png')
        protected_canvas.save(diagnostic/'protected-canvas.png')
        condition.save(diagnostic/'condition-full.png')
    full_source = None
    crop_box = None
    full_condition=condition.copy()
    prompt = request.get('prompt', '').strip()
    local_clothing=False
    if request.get('auto_prompt', True):
        if source is None:
            raise ValueError('Choose an image for automatic prompting / 自动提示词需要底图')
        description = caption(source, request['models'], device)
        import numpy as np
        rows, cols = np.where(np.asarray(condition)[:, :, 0] != 128)
        local_description = ''
        if len(rows):
            fitted = fit_canvas(source,condition.size,Image.Resampling.LANCZOS)
            crop = fitted.crop((int(cols.min()), int(rows.min()), int(cols.max()) + 1, int(rows.max()) + 1))
            local_description = caption(crop, request['models'], device)
            if materials is not None:
                details=[caption(detail,request['models'],device) for detail in detail_caption_regions(fitted,condition,materials)]
                descriptions=[local_description]+details
                clothing_words=('sweater','vest','dress','shirt','coat','outfit','fabric','clothing','garment','harness','uniform','jacket')
                local_clothing=sum(caption_has(description,clothing_words) for description in descriptions)>len(descriptions)/2
                local_description='; '.join(descriptions)
        prompt = make_prompt(description, local_description)
        if skin_canvas is not None and skin_canvas.getbbox():
            # The per-material pass below retains its own clothing/background
            # prompt; this accurately describes the newly allowed skin surface.
            prompt += ', original skin tone and texture, unchanged pose and anatomy'
        emit('caption', description=description, local_description=local_description,
             prompt=profile_arguments({'prompt': prompt}, profile)['prompt'])
    if not prompt:
        raise ValueError('Prompt is empty / 提示词为空')
    reported_prompt = profile_arguments({'prompt': prompt}, profile, request.get('auto_prompt', True))['prompt']
    if source and request.get('local_patch', True):
        import numpy as np
        rows, cols = np.where(np.asarray(condition)[:, :, 0] != 128)
        if len(rows):
            full_source = fit_canvas(source,condition.size,Image.Resampling.LANCZOS)
            tile = max(int(cols.max()) - int(cols.min()) + 1, int(rows.max()) - int(rows.min()) + 1)
            side = min(min(condition.size), max(plan['edge'], ((tile + 160 + 63) // 64) * 64))
            left = max(0, min(condition.width - side, (int(cols.min()) + int(cols.max()) + 1 - side) // 2))
            top = max(0, min(condition.height - side, (int(rows.min()) + int(rows.max()) + 1 - side) // 2))
            crop_box = (left, top, left + side, top + side)
            source, condition = full_source.crop(crop_box), condition.crop(crop_box)
            if materials is not None:
                materials = materials.crop(crop_box)
            emit('progress', message=f'Local patch inference / 局部生图 {side}×{side}; full output / 完整输出 {full_source.width}×{full_source.height}')
    def finalize_canvas(output):
        if skin_canvas is not None:
            output=constrained_skin_light(output,original_canvas,skin_canvas)
        if protected_canvas is not None:
            output=Image.composite(original_canvas,output,protected_canvas)
        return output
    def expand(patch):
        if full_source is None:
            output=patch.resize(original_canvas.size,Image.Resampling.LANCZOS) if original_canvas else patch
        else:
            output = full_source.copy()
            size=(crop_box[2]-crop_box[0],crop_box[3]-crop_box[1])
            patch=patch.resize(size,Image.Resampling.LANCZOS)
            yy,xx=np.mgrid[:size[1],:size[0]]
            margin=np.minimum.reduce((xx,yy,size[0]-1-xx,size[1]-1-yy))
            feather=Image.fromarray((np.clip(margin/64.,0,1)*255).astype(np.uint8))
            output.paste(patch,crop_box[:2],feather)
        # Always restore from the initial canvas, including after OOM downscaling
        # and optional refinement/background passes. Never restore a generated face.
        return finalize_canvas(output)
    def publish_candidate(shown,**values):
        okay=expected is None or independent_validation(shown,expected)
        emit('candidate',path=str(out),width=shown.width,height=shown.height,decoder_ok=okay,**values)
        return okay
    dtype = torch.float16 if gpu else torch.float32
    kwargs = dict(torch_dtype=dtype, local_files_only=True, use_safetensors=True)
    emit('progress', message='Loading local generation models / 加载本地生图模型')
    if control.is_dir():
        net = ControlNetModel.from_pretrained(str(control), **kwargs)
    else:
        control_config = control.parent if (control.parent / 'config.json').is_file() else package_path(request['models'],'configs/controlnet-'+family)
        net = ControlNetModel.from_single_file(str(control), config=str(control_config), **kwargs)
    if family == 'sdxl':
        cls = StableDiffusionXLControlNetImg2ImgPipeline if source else StableDiffusionXLControlNetPipeline
    else:
        cls = StableDiffusionControlNetImg2ImgPipeline if source else StableDiffusionControlNetPipeline
    tile_path = package_path(request['models'],'structure/tile-sd15')
    tile = None
    brightness = None
    if family == 'sd15' and (tile_path / 'diffusion_pytorch_model.bin').is_file():
        tile = ControlNetModel.from_pretrained(str(tile_path), torch_dtype=dtype, local_files_only=True, use_safetensors=False)
        emit('progress', message='QR + Tile ControlNet: preserving source detail / 双 ControlNet 保留底图细节')
    brightness_path = package_path(request['models'],'structure/brightness-sd15')
    if family == 'sd15' and (brightness_path / 'diffusion_pytorch_model.safetensors').is_file():
        brightness = ControlNetModel.from_pretrained(str(brightness_path), **kwargs)
        emit('progress', message='Local luminance guidance / 本地亮度辅助约束')
    networks = [net] + ([tile] if tile else []) + ([brightness] if brightness else [])
    load = dict(controlnet=networks if len(networks) > 1 else net, **kwargs)
    if family != 'sdxl':
        load.update(safety_checker=None, requires_safety_checker=False)
    variant = 'fp16' if base.is_dir() and any(base.rglob('*.fp16.safetensors')) else None
    if variant:
        load['variant'] = variant
    pipe = cls.from_pretrained(str(base), **load) if base.is_dir() else cls.from_single_file(str(base), config=str(config), **load)
    pipe.scheduler = DPMSolverMultistepScheduler.from_config(pipe.scheduler.config, algorithm_type='dpmsolver++', use_karras_sigmas=True)
    pipe.enable_vae_slicing()
    pipe.enable_vae_tiling()
    if gpu and plan['sequential']:
        pipe.enable_sequential_cpu_offload()
    elif gpu and plan['offload']:
        pipe.enable_model_cpu_offload()
    else:
        pipe.to(device)
    if not gpu:
        torch.set_num_threads(max(1, min(8, os.cpu_count() or 1)))
    if source and request.get('auto_background', False):
        original = full_source if full_source is not None else source
        mask = blank_background_mask(original)
        if mask is not None:
            mask=Image.fromarray(np.where(np.asarray(protected_canvas)>0,0,np.asarray(mask)).astype(np.uint8))
            emit('progress', message='Generating a matching background locally / 本地生成协调背景')
            bg_edge = plan['edge']
            factor = bg_edge / max(original.size)
            bg_size = tuple(max(256, int(v * factor) // 64 * 64) for v in original.size)
            bg_input = original.resize(bg_size, Image.Resampling.LANCZOS)
            neutral = Image.new('RGB', bg_size, (128,128,128))
            bg_controls = [neutral] * len(networks)
            bg_scales = [0.] * len(networks)
            with torch.inference_mode():
                background = infer(pipe, dict(prompt=background_prompt(prompt), negative_prompt='blurry, horror, grotesque, watermark, text, logo, person, face, head, hair, eyes, hands, arms, legs, body parts', image=neutral, control_image=bg_controls if len(networks)>1 else neutral, controlnet_conditioning_scale=bg_scales if len(networks)>1 else 0., strength=1., num_inference_steps=plan['steps'], guidance_scale=5.5, generator=torch.Generator(device='cpu').manual_seed(21731))).images[0]
            prepared = Image.composite(background.resize(original.size, Image.Resampling.LANCZOS), original, mask)
            if full_source is not None:
                full_source = prepared
                source = prepared.crop(crop_box)
            else:
                source = prepared
    width, height = condition.size
    seed = int(request.get('seed', -1))
    if seed < 0:
        seed = int.from_bytes(os.urandom(4), 'little') & 0x7fffffff
    out = Path(request['output']).resolve()
    out.parent.mkdir(parents=True, exist_ok=True)
    attempts = max(1, min(6, int(request.get('attempts', 6))))
    guided=request.get('gradient_guidance',False) and family=='sd15' and not plan['offload'] and supports_scanning_guidance(pipe.scheduler)
    if request.get('gradient_guidance',False) and not guided:
        emit('progress',message='Using texture ControlNet without VAE gradients at this memory/model setting / 当前显存或模型使用材质 ControlNet，关闭潜变量梯度引导')
    defaults=(.7,.85,1.05,1.15,1.25,1.4) if guided else (.6,.75,.9,1.05,1.2,1.4)
    weights = tuple(request.get('control_weights') or (defaults if request.get('preserve_clothes',True) else (1.2,1.45,1.7,1.9,2.1,2.3)))
    if len(weights)<attempts:raise ValueError('Missing retry control weights / 重试控制权重不足')
    last_oom=None
    try:
        for attempt in range(attempts):
            def progress_callback(pipeline, step, timestep, values):
                emit('step', attempt=attempt + 1, step=step + 1, total=getattr(pipeline,'num_timesteps',plan['steps']))
                return values
            arguments = dict(prompt=prompt, negative_prompt=request.get('negative') or 'low quality, blurry, watermark, text, logo, deformed face, distorted anatomy, horror, grotesque, organic holes, tentacles, spirals, chaotic repetitive pattern', num_inference_steps=plan['steps'], guidance_scale=5.5, controlnet_conditioning_scale=weights[attempt], control_guidance_start=0., control_guidance_end=.95, generator=torch.Generator(device='cpu').manual_seed(seed + attempt), callback_on_step_end=progress_callback)
            if source:
                fitted_source = fit_canvas(source, (width, height), Image.Resampling.LANCZOS)
                art_condition=rounded_finders(condition) if request.get('finder_shape')=='circle' else condition
                if request.get('organic_guide',False):art_condition=organic_condition(condition)
                if request.get('material_blueprint',False):art_condition=material_blueprint(condition,fitted_source,request.get('guide_style','adaptive'))
                controls = [art_condition] + ([fitted_source] if tile else []) + ([art_condition] if brightness else [])
                arguments.update(image=fitted_source, control_image=controls if len(controls) > 1 else art_condition, strength=min(.9, .72 + attempt * .03))
                if len(networks) > 1:
                    arguments.update(controlnet_conditioning_scale=[weights[attempt]] + ([.15] if tile else []) + ([.15] if brightness else []), control_guidance_start=[0.] + ([0] if tile else []) + ([0] if brightness else []), control_guidance_end=[.95] + ([.75] if tile else []) + ([.75] if brightness else []))
            else:
                arguments.update(image=condition, width=width, height=height)
            try:
                emit('progress', message=f'Generating / 生图 {attempt + 1}/{attempts}, ControlNet {weights[attempt]:.2f}')
                with torch.inference_mode():
                    if materials is not None:
                        current_materials=materials.resize((width,height),Image.Resampling.NEAREST)
                        bg_mask, cloth_mask = material_masks(current_materials)
                        skin_mask=manual_skin_mask(current_materials,condition) if manual_skin else None
                        image = fitted_source.copy()
                        # One pass avoids seams where an illustration is incorrectly
                        # split into clothing and background by the parser.
                        if request.get('preserve_clothes',True):
                            import numpy as np
                            combined=combined_material_mask(materials.resize((width,height),Image.Resampling.NEAREST))
                            code=np.asarray(condition)[:,:,0]!=128
                            editable=code&(np.asarray(combined)>128)
                            garment=local_clothing or ((np.asarray(cloth_mask)>128)&editable).sum()>editable.sum()*.55
                            passes=((combined,bool(garment)),)
                        else:
                            passes=((bg_mask,False),(cloth_mask,True))
                        passes=tuple((mask,cloth,False) for mask,cloth in passes)
                        if skin_mask is not None and skin_mask.getbbox():
                            passes+=((skin_mask,False,True),)
                        for mask, cloth, skin_pass in passes:
                            if mask.getbbox() is None:
                                continue
                            regional = dict(arguments)
                            regional['prompt'] = local_skin_prompt(prompt) if skin_pass else local_material_prompt(prompt, cloth)
                            regional['negative_prompt'] += ', face, head, hair, eyes, extra people, portrait, additional limbs, altered anatomy, fabric pattern, new garment, sticker, label, pasted patch, checkerboard, geometric tiles' if skin_pass else ', face, head, hair, eyes, extra people, portrait, skin, body parts, sticker, label, pasted patch, checkerboard, geometric tiles, new garment'
                            guided=request.get('gradient_guidance',False) and family=='sd15' and not plan['offload'] and supports_scanning_guidance(pipe.scheduler)
                            regional['strength'] = float(request.get('texture_strength',(.5 if cloth else .65) if guided else .42)) if request.get('preserve_clothes',True) else .85 if cloth else .92
                            if request.get('preserve_clothes',True) and len(networks)>1:
                                regional['controlnet_conditioning_scale']=[weights[attempt]]+([float(request.get('tile_scale',.4 if guided else .55))] if tile else [])+([.1] if brightness else [])
                            if skin_pass:
                                regional['strength']=.28
                                if len(networks)>1:
                                    regional['controlnet_conditioning_scale']=[weights[attempt]]+([.9] if tile else [])+([.1] if brightness else [])
                            scheduler=pipe.scheduler
                            guided=request.get('gradient_guidance',False) and family=='sd15' and not plan['offload'] and supports_scanning_guidance(pipe.scheduler)
                            natural=request.get('natural_materials',False) and guided and not cloth and not skin_pass
                            if natural:
                                hint=organic_condition(condition)
                                regional['control_image']=[hint]+([fitted_source] if tile else [])+([hint] if brightness else []) if len(networks)>1 else hint
                                weight=min(1.8,.8+attempt*.2)
                                regional['controlnet_conditioning_scale']=[weight]+([.3] if tile else [])+([.1] if brightness else []) if len(networks)>1 else weight
                                regional['strength']=.75
                                emit('progress',message='Generating coherent scene materials before center refinement / 先生成连贯场景材质，再修整编码中心')
                            if guided and not natural and not request.get('two_stage',False) and not request.get('latent_refine',False):
                                pipe.scheduler=scanning_scheduler(pipe,condition,fitted_source,float(request.get('gradient_scale',140000)),int(request.get('gradient_edge',1024)),request.get('finder_shape','square'),float(request.get('gradient_dark',.45)),float(request.get('gradient_light',.65)),float(request.get('gradient_locator',.4)),float(request.get('gradient_quiet',.55)),float(request.get('gradient_preservation',14000 if cloth else 7000)),int(request.get('gradient_start',850)))
                            try:
                                generated = infer(pipe, regional, skin_pass).images[0]
                            finally:
                                pipe.scheduler=scheduler
                            if request.get('diagnostics'):
                                generated.save(Path(request['diagnostics'])/f'initial-material-{attempt}.png')
                            if request.get('latent_refine',False) and guided:
                                generated=refine_latent(pipe,generated,condition,int(request.get('latent_steps',80)))
                            elif natural:
                                # Raise local readability before falling back
                                # to the more conspicuous stable structure.
                                organic=attempt<2
                                generated=refine_latent(pipe,generated,condition,80,
                                    dark=.46 if organic else .43,light=.62 if organic else .69,
                                    structure_weight=2.,texture_weight=.4,
                                    locator_structure='organic' if organic else 'stable',
                                    locator_dark=.42 if organic else .4,
                                    locator_light=.66 if attempt==0 else .69 if organic else .7)
                            if request.get('two_stage',False) and guided:
                                emit('progress',message='Refining natural candidate at erroneous centers / 从自然材质候选继续修整误码中心')
                                refinement=dict(regional)
                                refinement.update(image=generated,strength=float(request.get('refine_strength',.24)),num_inference_steps=80,
                                    controlnet_conditioning_scale=[.5]+([.4] if tile else [])+([.1] if brightness else []) if len(networks)>1 else .5,
                                    generator=torch.Generator(device='cpu').manual_seed(seed+attempt+4096))
                                new_guide=material_blueprint(condition,generated)
                                refinement['control_image']=[new_guide]+([generated] if tile else [])+([new_guide] if brightness else []) if len(networks)>1 else new_guide
                                preservation_reference=generated if request.get('refine_preserve_generated',False) else fitted_source
                                pipe.scheduler=scanning_scheduler(pipe,condition,preservation_reference,float(request.get('gradient_scale',700)),int(request.get('gradient_edge',768)),request.get('finder_shape','square'),float(request.get('gradient_dark',.45)),float(request.get('gradient_light',.65)),float(request.get('gradient_locator',.3)),float(request.get('gradient_quiet',.55)),float(request.get('gradient_preservation',2.)),int(request.get('gradient_start',850)))
                                try:
                                    generated=infer(pipe, refinement, skin_pass).images[0]
                                finally:
                                    pipe.scheduler=scheduler
                            import numpy as np
                            detected = np.asarray(parse_materials(generated, request['models'], device,tiled=False,protected_confidence=.85))
                            # Inspect only pixels that will actually reach the result.
                            edits = np.asarray(preserve_surroundings(Image.new('RGB',image.size,'white'),Image.new('RGB',image.size,'black'),condition))[:,:,0] > 128
                            edits &= np.asarray(mask) > 128
                            if request.get('diagnostics'):
                                diagnostic=Path(request['diagnostics']);diagnostic.mkdir(parents=True,exist_ok=True)
                                generated.save(diagnostic/f'generated-{attempt}-{cloth}.png')
                            # Dark patterned fabric is often mislabeled as hair;
                            # restoring those pixels erases otherwise correct bits.
                            # Face detection supplements skin/limb segmentation.
                            new_faces=np.asarray(face_guards(generated,request['models'],confidence=.65))>0
                            detected_skin=np.isin(detected,(12,13,14,15))
                            if skin_pass:
                                detected_skin &= np.asarray(skin_mask)==0
                            suspect=((detected==11)|detected_skin|new_faces)&edits
                            if suspect.sum() > max(32, edits.sum() * .005):
                                emit('progress', message='Restoring source pixels over suspected human features / 疑似新增五官或肢体处还原原图像素')
                                suspected=Image.fromarray(suspect.astype(np.uint8)*255)
                                generated=Image.composite(fitted_source,generated,suspected)
                            if skin_pass:
                                generated=constrained_skin_light(generated,fitted_source,skin_mask)
                            image = Image.composite(generated, image, mask)
                    else:
                        image = infer(pipe, arguments).images[0]
            except torch.OutOfMemoryError as error:
                # Retry at a smaller size with the same local models and conditions.
                last_oom=error
                gc.collect()
                if gpu:
                    torch.cuda.empty_cache()
                ratio = .75
                width, height = max(384, int(width * ratio) // 64 * 64), max(384, int(height * ratio) // 64 * 64)
                condition = condition.resize((width, height), Image.Resampling.NEAREST)
                if gpu and not plan['offload']:
                    pipe.to('cpu')
                    pipe.enable_model_cpu_offload()
                    plan['offload'] = True
                emit('progress', message=f'VRAM fallback / 显存不足，降档重试 {width}×{height}')
                continue
            last_oom=None
            if source:
                if request.get('preserve_palette',True):
                    image = matching_palette(image,fitted_source,float(request.get('shadow_floor',.38)))
                if materials is not None:
                    current_materials=materials.resize(image.size,Image.Resampling.NEAREST)
                    skin_mask=manual_skin_mask(current_materials,condition) if manual_skin else None
                    allowed=editable_material_mask(current_materials,skin_mask)
                    image=Image.composite(image,fitted_source,allowed)
                image = preserve_surroundings(image, fitted_source, condition)
            # Refine a readable scaffold with a smaller diffusion pass. Only the
            # material masks are composited; the person is never a text prompt
            # for a garment or scenery crop.
            if source and request.get('polish', False):
                for strength in (.4, .55):
                    natural = image.copy()
                    masks = material_masks(materials.resize((width,height),Image.Resampling.NEAREST)) if materials is not None else (None,None)
                    for mask, cloth in ((masks[0],False),(masks[1],True)):
                        if mask is None or mask.getbbox() is None:
                            continue
                        polished = dict(arguments)
                        polished.update(image=image, prompt=local_material_prompt(prompt,cloth), strength=strength,
                            negative_prompt=arguments['negative_prompt']+', face, hair, head, eyes, hands, skin, body parts, checkerboard, pixel grid, geometric tiles, concentric squares',
                            controlnet_conditioning_scale=[.65] + ([.2] if tile else []) + ([.1] if brightness else []) if len(networks)>1 else .65,
                            generator=torch.Generator(device='cpu').manual_seed(seed+attempt+101))
                        emit('progress',message=f'Refining natural material transitions / 二次生图衔接材质 {strength:.2f}')
                        with torch.inference_mode():
                            generated=infer(pipe, polished).images[0]
                        detected=np.asarray(parse_materials(generated,request['models'],device))
                        edits=np.asarray(mask)>0
                        if (np.isin(detected,(2,11,12,13,14,15))&edits).sum()>max(32,edits.sum()*.005):
                            continue
                        natural=Image.composite(generated,natural,mask)
                    shown=expand(natural);shown.save(out,format='PNG')
                    independent_ok=publish_candidate(shown,seed=seed+attempt,weight=weights[attempt],diffusion_refinement=strength)
                    reply=json.loads(sys.stdin.readline() or '{}')
                    if independent_ok and reply.get('accept'):
                        emit('result',path=str(out),prompt=reported_prompt,seed=seed+attempt,width=shown.width,height=shown.height,region=qr_region,diffusion_refinement=strength)
                        return
            shown = expand(image)
            if source and guided and request.get('natural_materials',False) and request.get('locator_points',True):
                try:
                    previous=shown
                    points=refine_locator_points(pipe,shown,full_condition,original_canvas,protected_canvas)
                    blended=transparent_locator_background(points,full_condition,original_canvas,protected_canvas)
                    if manual_skin:
                        points=finalize_canvas(points)
                        blended=finalize_canvas(blended)
                    if expected is not None and independent_validation(blended,expected):
                        shown=blended
                        emit('progress',message='Restoring source texture behind recognition points / 透出定位点后的原图纹理')
                    elif expected is None or independent_validation(points,expected):
                        shown=points
                    else:
                        shown=previous
                        emit('progress',message='Recognition refinement did not pass; retaining previous texture / 定位修整未通过，保留上一版纹理并继续校验')
                except torch.OutOfMemoryError as error:
                    last_oom=error
                    gc.collect();torch.cuda.empty_cache()
                    emit('progress',message='Locator refinement needs more free VRAM; retrying / 定位点修整显存不足，重试')
                    continue
            options=[shown]
            if original_canvas is not None and material_canvas is not None and expected is not None and request.get('preserve_clothes',True):
                textured=restore_fabric_detail(shown,original_canvas,material_canvas,protected_canvas)
                if independent_validation(textured,expected) and not np.array_equal(np.asarray(textured),np.asarray(shown)):
                    options.insert(0,textured)
                    emit('progress',message='Recovering original fine fabric detail / 恢复原衣物的细小织纹并重新校验')
            for displayed in options:
                displayed.save(out, format='PNG')
                independent_ok=publish_candidate(displayed,seed=seed+attempt,weight=weights[attempt])
                reply = json.loads(sys.stdin.readline() or '{}')
                if independent_ok and reply.get('accept'):
                    emit('result', path=str(out), prompt=reported_prompt, seed=seed + attempt, width=displayed.width, height=displayed.height, region=qr_region)
                    return
            for adjusted,radius in refine_module_light(image,condition,materials) if request.get('light_repair', False) else []:
                shown=expand(adjusted)
                shown.save(out,format='PNG')
                independent_ok=publish_candidate(shown,seed=seed+attempt,weight=weights[attempt],light_refinement=radius)
                reply=json.loads(sys.stdin.readline() or '{}')
                if independent_ok and reply.get('accept'):
                    emit('result',path=str(out),prompt=reported_prompt,seed=seed+attempt,width=shown.width,height=shown.height,region=qr_region,light_refinement=radius)
                    return
            # Restore readability, then re-diffuse the texture rather than exporting a grid patch.
            if source and request.get('natural_refine', False):
                candidates = list(contrast_refinements(image, condition))
                if candidates:
                    blueprint = candidates[-1][0]
                    for refinement in range(2):
                        polished = dict(arguments)
                        polished.update(image=blueprint, strength=.5 + refinement * .07, controlnet_conditioning_scale=[1.1] + ([.2] if tile else []) + ([.22] if brightness else []) if len(networks) > 1 else 1.1, generator=torch.Generator(device='cpu').manual_seed(seed + attempt + 100 + refinement))
                        emit('progress', message='Refining texture with diffusion / 第二轮生图融合纹理')
                        with torch.inference_mode():
                            natural = infer(pipe, polished).images[0]
                        natural = preserve_surroundings(natural, fitted_source, condition)
                        shown = expand(natural)
                        shown.save(out, format='PNG')
                        independent_ok=publish_candidate(shown,seed=seed+attempt,weight=weights[attempt],diffusion_refinement=refinement+1)
                        reply = json.loads(sys.stdin.readline() or '{}')
                        if independent_ok and reply.get('accept'):
                            emit('result', path=str(out), prompt=reported_prompt, seed=seed + attempt, width=shown.width, height=shown.height, diffusion_refinement=refinement + 1, region=qr_region)
                            return
            for refined, level in contrast_refinements(image, condition) if request.get('contrast_repair', False) else []:
                shown = expand(refined)
                shown.save(out, format='PNG')
                emit('progress', message=f'Local contrast refinement / 局部亮度修复 {level:.2f}')
                independent_ok=publish_candidate(shown,seed=seed+attempt,weight=weights[attempt],refinement=level)
                reply = json.loads(sys.stdin.readline() or '{}')
                if independent_ok and reply.get('accept'):
                    emit('result', path=str(out), prompt=reported_prompt, seed=seed + attempt, width=shown.width, height=shown.height, refinement=level, region=qr_region)
                    return
        if last_oom is not None:
            raise last_oom
        raise QrScanFailed('No scannable result after generation retries; adjust the image or prompt / 生图重试后仍未通过扫码，请调整位置、底图或提示词')
    finally:
        del pipe, net, tile, brightness, networks
        gc.collect()
        if gpu:
            torch.cuda.empty_cache()
        emit('released', message='Model memory released; worker exits / 模型资源已释放，推理进程退出')

def main():
    if hasattr(sys.stdout, 'reconfigure'):
        sys.stdout.reconfigure(encoding='utf-8')
    request = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8-sig'))
    mode = request['operation']
    if mode == 'probe':
        probe(request)
    elif mode == 'bootstrap':
        bootstrap(request)
    elif mode == 'download':
        download_models(request)
    elif mode == 'generate':
        generate(request)
    elif mode == 'validate':
        from PIL import Image
        with Image.open(request['input']) as image:
            emit('result',valid=independent_validation(image.convert('RGB'),request['expected']))
    else:
        raise ValueError('Unknown operation')

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        report_error(error)
        sys.exit(1)
