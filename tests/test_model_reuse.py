"""Small real-file reuse tests; no model weights or network are downloaded."""
from pathlib import Path
import errno,hashlib,importlib.util,json,os,tempfile,unittest
from unittest.mock import patch
spec=importlib.util.spec_from_file_location('reuse_worker',Path(__file__).resolve().parents[1]/'ai/local_qr.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)

class ReuseTests(unittest.TestCase):
    def setup_files(self,base,files):
        root=base/'new & 中文 models';old=base/'existing models';root.mkdir();old.mkdir()
        package={'family':'sd15','target':'base/example','repo':'repo/example','revision':'locked','files':[]}
        for name,data in files.items():package['files'].append({'local':name,'path':name,'size':len(data),'sha256':hashlib.sha256(data).hexdigest()})
        manifest=root/'engine/models.json';manifest.parent.mkdir();manifest.write_text(json.dumps({'packages':[package]}))
        request={'manifest':str(manifest),'models':str(root),'search_roots':[str(old)]}
        return root,old,package,request
    def write(self,root,name,data):
        path=root/'base/example'/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data);return path
    def test_complete_external_package_readonly_no_copy_or_http(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'config.json':b'configuration','weights.safetensors':b'weights'})
            for f in p['files']:self.write(old,f['local'],b'configuration' if f['local']=='config.json' else b'weights')
            before={str(p):p.read_bytes() for p in old.rglob('*') if p.is_file()}
            with patch.object(worker,'choose_sources',side_effect=AssertionError('HTTP for complete model')),patch.object(worker,'emit'):
                worker.download_models(request)
                self.assertEqual(worker.package_path(root,'base/example'),old/'base/example')
            self.assertFalse((root/'base/example/weights.safetensors').exists())
            self.assertEqual(before,{str(p):p.read_bytes() for p in old.rglob('*') if p.is_file()})
    def test_partial_external_hardlink_and_only_missing_download(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'config.json':b'config','weights.safetensors':b'weights'})
            source=self.write(old,'weights.safetensors',b'weights');calls=[]
            def get(url,dest,digest,size,label,progress=None):calls.append(label);Path(dest).write_bytes(b'config')
            with patch.object(worker,'choose_sources',return_value=['official']),patch.object(worker,'download',side_effect=get),patch.object(worker,'emit'):
                worker.download_models(request)
            self.assertEqual(calls,['base/example/config.json']);self.assertTrue(os.path.samefile(source,root/'base/example/weights.safetensors'))
            self.assertEqual(source.read_bytes(),b'weights')
    def test_same_name_same_size_wrong_hash_not_reused(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'weights.safetensors':b'correct'})
            source=self.write(old,'weights.safetensors',b'incorre');calls=[]
            def get(url,dest,digest,size,label,progress=None):calls.append(label);dest=Path(dest);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(b'correct')
            with patch.object(worker,'choose_sources',return_value=['official']),patch.object(worker,'download',side_effect=get),patch.object(worker,'emit'):
                worker.download_models(request)
            self.assertEqual(calls,['base/example/weights.safetensors']);self.assertEqual(source.read_bytes(),b'incorre')
    def test_force_hash_reads_again_cached_check_avoids_rehash(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'weights.safetensors':b'valid weights'})
            source=self.write(old,'weights.safetensors',b'valid weights')
            with patch.object(worker,'emit') as events:worker.download_models(request)
            self.assertTrue(any(c.args==('verification',) for c in events.call_args_list))
            with patch.object(worker,'emit') as events,patch.object(worker,'choose_sources',side_effect=AssertionError('HTTP on cache')):worker.download_models(request)
            self.assertFalse(any(c.args==('verification',) for c in events.call_args_list))
            with patch.object(worker,'emit') as events:worker.download_models(dict(request,force_hash=True))
            self.assertTrue(any(c.args==('verification',) for c in events.call_args_list))
            stat=source.stat();source.write_bytes(b'other weights');os.utime(source,ns=(stat.st_atime_ns,stat.st_mtime_ns))
            with patch.object(worker,'emit'),self.assertRaises(IOError):worker.package_path(root,'base/example')
    def test_changed_reference_removed_and_repaired_only_in_root(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'weights.safetensors':b'valid'})
            source=self.write(old,'weights.safetensors',b'valid')
            with patch.object(worker,'emit'):worker.download_models(request)
            source.write_bytes(b'wrong')
            def get(url,dest,digest,size,label,progress=None):dest=Path(dest);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(b'valid')
            with patch.object(worker,'emit'),patch.object(worker,'choose_sources',return_value=['official']),patch.object(worker,'download',side_effect=get):worker.download_models(request)
            self.assertEqual(source.read_bytes(),b'wrong');self.assertEqual(worker.package_path(root,'base/example'),root/'base/example')
            self.assertEqual(json.loads((root/'model-reuse.json').read_text(encoding='utf-8'))['packages'],{})
    def test_cross_disk_partial_weights_stop_before_http(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'config.json':b'config','weights.safetensors':b'weights'})
            source=self.write(old,'weights.safetensors',b'weights')
            with patch.object(worker.os,'link',side_effect=OSError(errno.EXDEV,'different disks')),patch.object(worker,'choose_sources',side_effect=AssertionError('duplicate HTTP')),patch.object(worker,'emit'),self.assertRaisesRegex(IOError,'not copied or redownloaded'):
                worker.download_models(request)
            self.assertEqual(source.read_bytes(),b'weights');self.assertFalse((root/'base/example/weights.safetensors').exists())
    def test_inference_complete_without_readme_only_downloads_small_doc(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'config.json':b'config','weights.safetensors':b'weights','README.md':b'license docs'})
            self.write(old,'config.json',b'config');self.write(old,'weights.safetensors',b'weights');calls=[]
            def get(url,dest,digest,size,label,progress=None):calls.append(label);dest=Path(dest);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(b'license docs')
            with patch.object(worker,'emit'),patch.object(worker,'choose_sources',return_value=['official']),patch.object(worker,'download',side_effect=get):worker.download_models(request)
            self.assertEqual(calls,['base/example/README.md']);self.assertEqual(worker.package_path(root,'base/example'),old/'base/example');self.assertFalse((old/'base/example/README.md').exists())
    def test_reference_empty_or_tampered_expected_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'weights.safetensors':b'weights'});self.write(old,'weights.safetensors',b'weights')
            with patch.object(worker,'emit'):worker.download_models(request)
            record=json.loads((root/'model-reuse.json').read_text(encoding='utf-8'));record['packages']['base/example']['files']=[];(root/'model-reuse.json').write_text(json.dumps(record),encoding='utf-8')
            with self.assertRaisesRegex(IOError,'Invalid model reference'):worker.package_path(root,'base/example')
    def test_only_selected_base_skips_base_weight_not_control(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'model_index.json':b'{}','weights.safetensors':b'base'})
            control=dict(p,target='controlnet/example',repo='repo/control',files=[dict(p['files'][1],sha256=hashlib.sha256(b'ctrl').hexdigest(),size=4)])
            Path(request['manifest']).write_text(json.dumps({'packages':[p,control]}));calls=[]
            def get(url,dest,digest,size,label,progress=None):calls.append(label);dest=Path(dest);dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(b'ctrl' if label.startswith('controlnet/') else b'{}')
            with patch.object(worker,'emit'),patch.object(worker,'choose_sources',return_value=['official']),patch.object(worker,'download',side_effect=get):worker.download_models(dict(request,skip_weights=['base']))
            self.assertEqual(set(calls),{'configs/sd15/model_index.json','controlnet/example/weights.safetensors'})
    def test_complete_partial_no_http_before_probe(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'weights.safetensors':b'weights'});partial=self.write(root,'weights.safetensors.part',b'weights')
            with patch.object(worker,'emit'),patch.object(worker,'choose_sources',side_effect=AssertionError('HTTP probe for complete partial')):
                worker.download_models(request)
            self.assertFalse(partial.exists());self.assertEqual((root/'base/example/weights.safetensors').read_bytes(),b'weights')
    def test_valid_json_wrong_cache_types_are_rechecked(self):
        for saved in ({'files':[]},{'files':None},[],None):
            with self.subTest(saved=saved),tempfile.TemporaryDirectory() as tmp:
                root,old,p,request=self.setup_files(Path(tmp),{'weights.safetensors':b'weights'});self.write(root,'weights.safetensors',b'weights');(root/'model-hashes.json').write_text(json.dumps(saved))
                with patch.object(worker,'emit') as events,patch.object(worker,'choose_sources',side_effect=AssertionError('HTTP on valid local file')):worker.download_models(request)
                self.assertTrue(any(c.args==('verification',) for c in events.call_args_list))
                self.assertIsInstance(json.loads((root/'model-hashes.json').read_text(encoding='utf-8'))['files'],dict)
    def test_valid_json_wrong_reuse_types_are_repaired(self):
        for saved in ({'packages':[]},{'packages':None},{'packages':{'base/example':[]}},[],None):
            with self.subTest(saved=saved),tempfile.TemporaryDirectory() as tmp:
                root,old,p,request=self.setup_files(Path(tmp),{'weights.safetensors':b'weights'});self.write(root,'weights.safetensors',b'weights');(root/'model-reuse.json').write_text(json.dumps(saved))
                with patch.object(worker,'emit'),patch.object(worker,'choose_sources',side_effect=AssertionError('HTTP on valid local file')):worker.download_models(request)
                self.assertEqual(json.loads((root/'model-reuse.json').read_text(encoding='utf-8'))['packages'],{})
    def test_wrong_cached_entry_type_is_rehashed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root,old,p,request=self.setup_files(Path(tmp),{'weights.safetensors':b'weights'});source=self.write(root,'weights.safetensors',b'weights');(root/'model-hashes.json').write_text(json.dumps({'files':{str(source.resolve()):[]}}))
            with patch.object(worker,'emit') as events,patch.object(worker,'choose_sources',side_effect=AssertionError('HTTP on valid local file')):worker.download_models(request)
            self.assertTrue(any(c.args==('verification',) for c in events.call_args_list))
if __name__=='__main__':unittest.main()
