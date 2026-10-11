"""Real small files and offline HTTP streams; aggregate completion must be verified."""
from pathlib import Path
import errno,hashlib,importlib.util,io,json,os,tempfile,threading,unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('progress_worker',Path(__file__).resolve().parents[1]/'ai/local_qr.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)

class Response(io.BytesIO):
    def __init__(self,data,status=200,headers=None):
        super().__init__(data);self.status=status;self.headers=headers or {'Content-Length':str(len(data))}

class ProgressTests(unittest.TestCase):
    def setup_files(self,folder,files):
        root=Path(folder)/'owned';old=Path(folder)/'external';root.mkdir();old.mkdir()
        p={'family':'sd15','target':'base/example','repo':'repo/example','revision':'locked','files':[
            {'local':name,'path':name,'size':len(data),'sha256':hashlib.sha256(data).hexdigest()} for name,data in files.items()]}
        manifest=root/'engine/models.json';manifest.parent.mkdir();manifest.write_text(json.dumps({'packages':[p]}))
        return root,old,p,{'models':str(root),'manifest':str(manifest),'search_roots':[str(old)]}
    def write(self,root,name,data):
        dest=root/'base/example'/name;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(data);return dest
    def events(self,mock,event='overall'):
        return [c.kwargs for c in mock.call_args_list if c.args==(event,)]
    def no_ready(self,mock):
        self.assertFalse(any(v.get('ready') for v in self.events(mock)+self.events(mock,'result')))
        self.assertFalse(any(v.get('percent')==100 for v in self.events(mock)))
    def test_cache_hardlink_and_resumed_bytes_share_total(self):
        files={'cached.json':b'cached','weights.safetensors':b'reused','new.json':b'0123456789'}
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(tmp,files)
            self.write(root,'cached.json',files['cached.json']);source=self.write(old,'weights.safetensors',files['weights.safetensors'])
            self.write(root,'new.json.part',files['new.json'][:4]);calls=[]
            def opened(req,timeout):
                calls.append(req);self.assertEqual(req.get_header('Range'),'bytes=4-')
                return Response(files['new.json'][4:],206,{'Content-Range':'bytes 4-9/10','Content-Length':'6'})
            with patch.object(worker,'choose_sources',return_value=['https://offline.invalid']),patch.object(worker.urllib.request,'urlopen',side_effect=opened),patch.object(worker,'emit') as events:
                worker.download_models(request)
            values=self.events(events);final=values[-1]
            self.assertEqual(len(calls),1);self.assertTrue(os.path.samefile(source,root/'base/example/weights.safetensors'))
            self.assertEqual((final['completed_files'],final['total_files']),(3,3))
            self.assertEqual((final['downloaded_bytes'],final['verified_bytes'],final['total_bytes']),(22,22,22))
            self.assertEqual((final['phase'],final['percent'],final['ready']),('ready',100,True))
            self.assertTrue(any(v['completed_files']==2 and v['downloaded_bytes']>=16 and not v['ready'] for v in values))
            self.assertTrue(all(v['percent']<100 for v in values if not v['ready']))
            self.assertTrue(self.events(events,'result')[-1]['ready'])
    def test_complete_external_counts_without_http_or_weight_copy(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(tmp,{'config.json':b'config','weights.safetensors':b'weights'})
            self.write(old,'config.json',b'config');self.write(old,'weights.safetensors',b'weights')
            with patch.object(worker,'choose_sources',side_effect=AssertionError('HTTP on complete reference')),patch.object(worker,'emit') as events:
                worker.download_models(request)
            final=self.events(events)[-1]
            self.assertEqual((final['completed_files'],final['downloaded_bytes'],final['total_bytes']),(2,13,13))
            self.assertTrue(final['ready']);self.assertFalse((root/'base/example/weights.safetensors').exists())
    def test_inspection_missing_file_does_not_claim_ready(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(tmp,{'cached.json':b'cached','new.json':b'new'})
            self.write(root,'cached.json',b'cached')
            with patch.object(worker,'choose_sources',side_effect=AssertionError('HTTP in inspection')),patch.object(worker,'emit') as events:
                worker.download_models(dict(request,inspection_only=True))
            self.no_ready(events);final=self.events(events)[-1]
            self.assertEqual((final['phase'],final['completed_files'],final['total_files']),('incomplete',1,2))
            self.assertEqual(self.events(events,'result')[-1]['missing'],1)
    def test_concurrent_full_length_bad_hash_never_completes(self):
        files={'good.json':b'good','bad.json':b'good'};entered=threading.Barrier(2)
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(tmp,files);first={};lock=threading.Lock()
            def opened(req,timeout):
                name=req.full_url.rsplit('/',1)[-1]
                with lock:initial=name not in first;first[name]=True
                if initial:entered.wait(timeout=5)
                return Response(b'bad!' if name=='bad.json' else b'good')
            with patch.object(worker,'choose_sources',return_value=['https://offline.invalid']),patch.object(worker.urllib.request,'urlopen',side_effect=opened),patch.object(worker.time,'sleep'),patch.object(worker,'emit') as events:
                with self.assertRaisesRegex(IOError,'SHA-256 mismatch'):worker.download_models(request)
            self.no_ready(events);self.assertEqual(self.events(events)[-1]['phase'],'failed')
            self.assertEqual(self.events(events)[-1]['completed_files'],1)
            self.assertEqual((root/'base/example/good.json').read_bytes(),b'good')
            self.assertFalse((root/'base/example/bad.json').exists())
            self.assertFalse(self.events(events,'result'))
    def test_cancel_exception_emits_no_ready_result(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(tmp,{'new.json':b'new content'})
            def cancelled(url,target,digest,size,label,progress=None):
                progress(label,size//2,'downloading');raise KeyboardInterrupt('cancelled')
            with patch.object(worker,'choose_sources',return_value=['offline']),patch.object(worker,'download',side_effect=cancelled),patch.object(worker,'emit') as events:
                with self.assertRaises(KeyboardInterrupt):worker.download_models(request)
            self.no_ready(events);self.assertEqual(self.events(events)[-1]['phase'],'failed');self.assertFalse(self.events(events,'result'))
    def test_cached_file_changed_during_other_download_fails_final_gate(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(tmp,{'cached.json':b'correct','new.json':b'new'})
            cached=self.write(root,'cached.json',b'correct');before=cached.stat()
            def downloaded(url,target,digest,size,label,progress=None):
                Path(target).write_bytes(b'new');cached.write_bytes(b'changed');os.utime(cached,ns=(before.st_atime_ns,before.st_mtime_ns))
            with patch.object(worker,'choose_sources',return_value=['offline']),patch.object(worker,'download',side_effect=downloaded),patch.object(worker,'emit') as events:
                with self.assertRaisesRegex(IOError,'Required model verification failed'):worker.download_models(request)
            self.no_ready(events);self.assertFalse(self.events(events,'result'))
            self.assertEqual(self.events(events)[-1]['completed_files'],1)
    def test_cross_disk_failure_then_same_disk_success_is_not_blocked(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(tmp,{'config.json':b'config','weights.safetensors':b'weights'})
            other=Path(tmp)/'same-disk';other.mkdir();self.write(root,'config.json',b'config')
            cross=self.write(old,'weights.safetensors',b'weights');same=self.write(other,'weights.safetensors',b'weights');link=os.link
            def linking(source,target):
                if Path(source)==cross:raise OSError(errno.EXDEV,'different disks')
                return link(source,target)
            with patch.object(worker,'package_candidates',return_value=[cross.parent,same.parent]),patch.object(worker.os,'link',side_effect=linking),patch.object(worker,'choose_sources',side_effect=AssertionError('duplicate network')),patch.object(worker,'emit') as events:
                worker.download_models(request)
            self.assertTrue(os.path.samefile(same,root/'base/example/weights.safetensors'));self.assertTrue(self.events(events)[-1]['ready'])
    def test_fully_received_bytes_stay_under_100_until_verify(self):
        p={'target':'example','files':[{'local':'a','size':10},{'local':'b','size':20}]}
        with patch.object(worker,'emit') as events:
            progress=worker.ModelProgress([p]);progress.update('example/a',10,'verifying');progress.update('example/b',20,'verifying')
            self.assertEqual(self.events(events)[-1]['downloaded_bytes'],30);self.no_ready(events)
            self.assertEqual(self.events(events)[-1]['completed_files'],0)
            with self.assertRaises(IOError):progress.finish(True)
    def test_pip_unknown_total_does_not_fake_percentage(self):
        result=type('Process',(),{'returncode':0,'stdout':''})()
        with patch.object(worker.subprocess,'run',return_value=result),patch.object(worker,'emit') as events:
            worker.pip('private-python',['Pillow==12.0.0'])
        info=self.events(events,'environment')
        self.assertEqual(info,[{'phase':'installing','total_bytes':None,'percent':None}])
    def test_bootstrap_successful_pip_without_pinned_versions_is_not_ready(self):
        def downloaded(url,target,expected):
            with worker.zipfile.ZipFile(target,'w') as archive:
                for name in ('python.exe','python311.dll','python311.zip') if url==worker.PYTHON_URL else ('pip/__main__.py',):archive.writestr(name,b'fixture')
        def version(name):return '0.0.0' if name=='Pillow' else worker.VERSIONS[name]
        with tempfile.TemporaryDirectory() as tmp,patch.object(worker,'download',side_effect=downloaded),patch.object(worker,'dependency_mirror',return_value=False),patch.object(worker,'has_nvidia',return_value=False),patch.object(worker,'pip'),patch.object(worker.importlib.metadata,'version',side_effect=version),patch.object(worker,'emit') as events:
            with self.assertRaisesRegex(RuntimeError,'Environment verification failed'):worker.bootstrap({'runtime':str(Path(tmp)/'runtime')})
            self.assertFalse(any(v.get('phase')=='ready' for v in self.events(events,'environment')))
            self.assertFalse(self.events(events,'result'))
            self.assertTrue(all(v['total_bytes'] is None and v['percent'] is None for v in self.events(events,'environment')))

if __name__=='__main__':unittest.main()
