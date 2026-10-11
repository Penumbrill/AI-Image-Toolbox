"""Offline download routing, resume telemetry and cache regression checks."""
from pathlib import Path
import hashlib
import importlib.util
import io
import json
import tempfile
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('download_worker',Path(__file__).resolve().parents[1]/'ai/local_qr.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)

class Response(io.BytesIO):
    def __init__(self,data,status=200,headers=None):
        super().__init__(data);self.status=status;self.headers={} if headers is None else headers

class DownloadTests(unittest.TestCase):
    def test_mainland_uses_reachable_mirror_even_when_official_faster(self):
        def probe(url):return {'reachable':True,'latency_ms':10 if url.startswith('official') else 100}
        with patch.object(worker,'endpoint_probe',side_effect=probe),patch.object(worker,'emit') as events:
            self.assertEqual(worker.choose_sources({'prefer_mirror':True},'official','mirror','/small'),['mirror','official'])
        self.assertEqual(events.call_args.kwargs['reason'],'region')
    def test_other_region_selects_lowest_reachable_latency(self):
        with patch.object(worker,'endpoint_probe',side_effect=lambda url:{'reachable':True,'latency_ms':100 if url.startswith('official') else 20}),patch.object(worker,'emit'):
            self.assertEqual(worker.choose_sources({'prefer_mirror':False},'official','mirror','/small'),['mirror','official'])
    def test_unavailable_mirror_does_not_override_reachable_official(self):
        with patch.object(worker,'endpoint_probe',side_effect=lambda url:{'reachable':url.startswith('official'),'latency_ms':25 if url.startswith('official') else None}),patch.object(worker,'emit'):
            self.assertEqual(worker.choose_sources({'prefer_mirror':True},'official','mirror','/small'),['official','mirror'])
    def test_manual_source_never_probes_or_overrides_user(self):
        with patch.object(worker,'endpoint_probe',side_effect=AssertionError('unexpected probe')):
            self.assertEqual(worker.choose_sources({'source':'official'},'official','mirror','/small'),['official'])
            self.assertEqual(worker.choose_sources({'source':'mirror'},'official','mirror','/small'),['mirror'])
    def test_endpoint_probe_reads_only_one_byte_with_short_timeout(self):
        response=Response(b'pinned config',headers={'Content-Length':'13'})
        with patch.object(worker.urllib.request,'urlopen',return_value=response) as opened:
            self.assertTrue(worker.endpoint_probe('https://example.invalid/locked-config')['reachable'])
        self.assertEqual(opened.call_args.kwargs['timeout'],4)
        self.assertIsNone(opened.call_args.args[0].get_header('Range'))
    def test_unknown_total_has_no_percentage_until_verified(self):
        data=b'unknown length content';digest=hashlib.sha256(data).hexdigest()
        with tempfile.TemporaryDirectory() as folder,patch.object(worker.urllib.request,'urlopen',return_value=Response(data)),patch.object(worker,'emit') as events:
            worker.download('https://example.invalid/data',Path(folder)/'model',digest)
        progress=[c.kwargs for c in events.call_args_list if c.args==('download',)]
        self.assertTrue(any(p['phase']=='verifying' for p in progress))
        for value in progress:
            if value['phase']!='verified':self.assertIsNone(value['percent'])
        self.assertEqual(progress[-1]['percent'],100)
    def test_resume_speed_counts_only_new_bytes(self):
        data=b'0123456789abcdef';digest=hashlib.sha256(data).hexdigest()
        with tempfile.TemporaryDirectory() as folder:
            target=Path(folder)/'model';target.with_name('model.part').write_bytes(data[:10])
            response=Response(data[10:],206,{'Content-Range':'bytes 10-15/16','Content-Length':'6'})
            with patch.object(worker.urllib.request,'urlopen',return_value=response),patch.object(worker,'emit') as events,patch.object(worker.time,'monotonic',side_effect=[1.,2.,3.]):
                worker.download('https://example.invalid/model',target,digest,len(data))
            verified=events.call_args.kwargs
            self.assertEqual(verified['transferred'],6);self.assertEqual(verified['bytes'],16);self.assertEqual(verified['speed'],3.)
            states=[c.kwargs['phase'] for c in events.call_args_list]
            self.assertLess(states.index('verifying'),states.index('verified'))
    def test_complete_partial_is_verified_without_network(self):
        data=b'complete staged file';digest=hashlib.sha256(data).hexdigest()
        with tempfile.TemporaryDirectory() as folder:
            target=Path(folder)/'model';target.with_name('model.part').write_bytes(data)
            with patch.object(worker.urllib.request,'urlopen',side_effect=AssertionError('HTTP on complete partial')),patch.object(worker,'emit'):
                worker.download('https://example.invalid/model',target,digest,len(data))
            self.assertEqual(target.read_bytes(),data)
    def test_cached_models_do_not_probe_network(self):
        data=b'cached bytes'
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);target=root/'base'/'example'/'config.json';target.parent.mkdir(parents=True);target.write_bytes(data)
            manifest={'packages':[{'family':'sd15','target':'base/example','repo':'repo/example','revision':'locked','files':[{'local':'config.json','path':'config.json','size':len(data),'sha256':hashlib.sha256(data).hexdigest()}]}]}
            path=root/'manifest.json';path.write_text(json.dumps(manifest))
            with patch.object(worker,'choose_sources',side_effect=AssertionError('probe on cached models')),patch.object(worker.urllib.request,'urlopen',side_effect=AssertionError('network on cached models')),patch.object(worker,'emit') as emitted:
                worker.download_models({'manifest':str(path),'models':str(root)})
            self.assertTrue(emitted.call_args.kwargs['cached'])
    def test_corrupt_cached_file_is_downloaded_and_source_fallback_works(self):
        data=b'correct bytes'
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);target=root/'base'/'example'/'config.json';target.parent.mkdir(parents=True);target.write_bytes(b'corrupt bytes')
            manifest={'packages':[{'family':'sd15','target':'base/example','repo':'repo/example','revision':'locked','files':[{'local':'config.json','path':'config.json','size':len(data),'sha256':hashlib.sha256(data).hexdigest()}]}]}
            path=root/'manifest.json';path.write_text(json.dumps(manifest));attempts=[]
            def download(url,destination,digest,size,label,progress=None):
                attempts.append(url)
                if url.startswith('mirror'):raise IOError('unavailable')
                Path(destination).write_bytes(data)
            with patch.object(worker,'choose_sources',return_value=['mirror','official']),patch.object(worker,'download',side_effect=download),patch.object(worker,'emit'):
                worker.download_models({'manifest':str(path),'models':str(root)})
            self.assertEqual(len(attempts),2);self.assertEqual(target.read_bytes(),data)
    def test_torch_index_stays_official_when_dependency_mirror_selected(self):
        result=type('Process',(),{'returncode':0,'stdout':''})()
        with patch.object(worker.subprocess,'run',return_value=result) as run,patch.object(worker,'emit'):
            worker.pip('private-python',['torch==2.8.0+cu128','--index-url','https://download.pytorch.org/whl/cu128'],True)
        command=run.call_args.args[0]
        self.assertEqual(command.count('--index-url'),1);self.assertNotIn('https://pypi.tuna.tsinghua.edu.cn/simple',command)

if __name__=='__main__':unittest.main()
