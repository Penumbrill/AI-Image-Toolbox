"""Offline control-policy, download-integrity and environment-path checks."""
from pathlib import Path
import hashlib
import importlib.util
import io
import tempfile
import types
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('local_qr',Path(__file__).resolve().parents[1]/'ai/local_qr.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)

class Response(io.BytesIO):
    def __init__(self,content,status=200,headers=None):
        super().__init__(content);self.status=status;self.headers=headers or {'Content-Length':str(len(content))}

class WorkerTests(unittest.TestCase):
    def test_imported_qr_content_is_bound_exactly_without_url_normalization(self):
        import numpy as np
        import zxingcpp
        from PIL import Image
        for payload in ('https://example.com/contact?a=1#card',
                        'BEGIN:VCARD\nVERSION:3.0\nFN:Example Contact\nEND:VCARD',
                        '  postcard text with spaces\n'):
            image=Image.fromarray(np.asarray(zxingcpp.write_barcode(
                zxingcpp.BarcodeFormat.QRCode,payload,width=640,height=640))).convert('RGB')
            self.assertEqual(worker.control_payload(image,payload),payload)
            self.assertTrue(worker.independent_validation(image,payload))
            with self.assertRaisesRegex(ValueError,'content mismatch') as error:
                worker.control_payload(image,payload+' changed')
            self.assertNotIn(payload,str(error.exception))
        with self.assertRaisesRegex(ValueError,'template'):
            worker.control_payload(Image.new('RGB',(640,640),'white'),'contact')

    def test_independent_decoder_rejects_wrong_payload_and_accepts_colored_qr(self):
        import numpy as np
        import zxingcpp
        from PIL import Image
        target='https://example.com/art?id=123#details'
        bitmap=np.asarray(zxingcpp.write_barcode(zxingcpp.BarcodeFormat.QRCode,target,width=1200,height=1200))
        colors=np.where((bitmap<128)[...,None],np.array([65,100,145]),np.array([190,215,235])).astype(np.uint8)
        image=Image.fromarray(colors)
        self.assertTrue(worker.independent_validation(image,target))
        self.assertFalse(worker.independent_validation(image,'https://example.com/art?id=123'))
        self.assertFalse(worker.independent_validation(Image.new('RGB',image.size,'white'),target))

    def test_editable_material_boundary_does_not_restore_source_grid(self):
        import numpy as np
        from PIL import Image
        labels=np.zeros((96,96),dtype=np.uint8)
        labels[:,48:]=4
        coverage=np.asarray(worker.combined_material_mask(Image.fromarray(labels)))
        self.assertGreaterEqual(int(coverage.min()),254)
        labels[40:56,40:56]=11
        coverage=np.asarray(worker.combined_material_mask(Image.fromarray(labels)))
        self.assertEqual(int(coverage[40:56,40:56].max()),0)
        self.assertEqual(int(coverage[35:61,35:61].max()),0)

    def test_policy_available_memory(self):
        self.assertTrue(worker.policy(8,7)['offload'])
        self.assertFalse(worker.policy(24,20)['offload'])
        self.assertTrue(worker.policy(24,20)['quality'])
        self.assertTrue(worker.policy(24,3)['sequential'])
        self.assertFalse(worker.policy(24,3)['quality'])
        self.assertTrue(worker.policy(24,20,preference='low')['offload'])
    def test_directory_escape(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(ValueError):worker.inside(root,'../outside')
    def test_material_prompt_does_not_generate_people(self):
        prompt=worker.make_prompt('a woman with long hair wearing a sweater','a grey knitted shirt')
        garment=worker.local_material_prompt(prompt,True)
        self.assertIn('yarn',garment)
        self.assertIn('original fabric colors',garment)
        self.assertNotIn('woman',garment)
        self.assertNotIn('hair',garment)
        self.assertNotIn('face',garment)
        self.assertIn('street',worker.background_prompt('anime illustration of a city street'))
        self.assertIn('quiet room',worker.background_prompt('room with window and floral sweater'))
        self.assertIn('illustration',worker.visual_style('anime drawing'))
        self.assertIn('photograph',worker.visual_style('portrait photograph'))
        self.assertIn('illustration',worker.make_prompt('a woman in a dress','an anime character in a white dress'))
        self.assertIn('existing leopard',worker.local_material_prompt(worker.make_prompt('a woman wearing a leopard print shirt'),True))
        self.assertNotIn('floral',worker.local_material_prompt(worker.make_prompt('a woman wearing a grey sweater'),True))
        scenery=worker.local_material_prompt('a woman in a red hat',False)
        self.assertNotIn('room',scenery)
        self.assertNotIn('woman',scenery)
        self.assertIn('unchanged scene',scenery)
        mixed=worker.make_prompt('a woman wearing a wool sweater','a building behind a tree')
        self.assertIn('existing knitted wool yarn',mixed)
        self.assertIn('existing architecture',mixed)
        self.assertIn('existing foliage',mixed)
        plain=worker.local_material_prompt('a character wearing a white uniform',True)
        self.assertNotIn('woven',plain);self.assertNotIn('wool',plain)
    def test_caption_hallucinations_do_not_direct_new_objects_or_colors(self):
        prompt=worker.make_prompt('a girl holding a bag in the room','a character with pink hair and a pink hat wearing a white dress')
        self.assertNotIn('holding',prompt)
        self.assertNotIn('bag',prompt)
        self.assertNotIn('pink',prompt)
        self.assertIn('original fabric palette',prompt)
        street=worker.make_prompt('a woman in a city street','a street with buildings')
        self.assertIn('architecture',street)
        self.assertNotIn('foliage',street)
        self.assertNotIn('branches',worker.local_material_prompt(street,False))
        floral=worker.make_prompt('a woman wearing a shirt with flowers','a floral dress')
        self.assertIn('floral fabric pattern',floral)
        self.assertNotIn('foliage',floral)
        trees=worker.make_prompt('a street with buildings and trees')
        self.assertIn('foliage',trees)
        self.assertFalse(worker.caption_has('investment research',('vest','sea')))
    def test_reader_threshold_goldens_and_dynamic_range_boundary(self):
        import numpy as np
        gradient=np.repeat((40+np.arange(48)*4).astype(np.uint8)[None,:],48,0)
        cpp=np.repeat(np.array([113,115,118,150,152,154],dtype=np.uint8),8)
        java=np.repeat(np.array([118,118,118,150,150,150],dtype=np.uint8),8)
        self.assertTrue(np.array_equal(worker.cpp_threshold_map(gradient),np.repeat(cpp[None,:],48,0)))
        self.assertTrue(np.array_equal(worker.java_threshold_map(gradient),np.repeat(java[None,:],48,0)))
        yy,xx=np.mgrid[:48,:48]
        low=(100+(xx+yy)%2*24).astype(np.uint8)
        high=(100+(xx+yy)%2*25).astype(np.uint8)
        self.assertTrue((worker.cpp_threshold_map(low)==0).all())
        self.assertTrue((worker.java_threshold_map(low)==50).all())
        self.assertTrue((worker.cpp_threshold_map(high)==112).all())
        self.assertTrue((worker.java_threshold_map(high)==112).all())
    def test_local_locator_refinement_keeps_surroundings_and_source_protection(self):
        import numpy as np
        import zxingcpp
        from PIL import Image,ImageDraw
        matrix=np.asarray(zxingcpp.write_barcode(zxingcpp.BarcodeFormat.QRCode,'https://example.com/local',quiet_zone=0))
        code=Image.fromarray(matrix).resize((matrix.shape[1]*16,matrix.shape[0]*16),Image.Resampling.NEAREST).convert('RGB')
        condition=Image.new('RGB',(1024,768),(128,128,128));condition.paste(code,(64,64))
        source=Image.new('RGB',condition.size,(125,160,195));candidate=Image.new('RGB',condition.size,(150,175,200))
        protected=Image.new('L',condition.size);ImageDraw.Draw(protected).rectangle((64,64,100,100),fill=255)
        with patch.object(worker,'emit'),patch.object(worker,'refine_latent',side_effect=lambda pipe,image,*args,**kwargs:Image.new('RGB',image.size,'white')) as refinement:
            result=worker.refine_locator_points(None,candidate,condition,source,protected)
        self.assertEqual(sum(call.kwargs['locator_structure']=='dots' for call in refinement.call_args_list),3)
        self.assertTrue(any(call.kwargs['locator_structure']=='alignment' for call in refinement.call_args_list))
        self.assertEqual(result.size,candidate.size)
        self.assertEqual(result.getpixel((80,80)),source.getpixel((80,80)))
        self.assertEqual(result.getpixel((1000,700)),candidate.getpixel((1000,700)))
        for call in refinement.call_args_list:
            self.assertEqual(call.args[1].size,(512,512))
            self.assertEqual(call.args[2].size,(512,512))
            self.assertEqual(worker.latent_refinement_divisor(call.args[2]),1)
            self.assertIn(call.kwargs['locator_structure'],('dots','alignment'))
    def test_detail_caption_avoids_protected_face_and_empty_sky(self):
        from PIL import Image,ImageDraw
        condition=Image.new('RGB',(512,512),'white')
        source=Image.new('RGB',condition.size,(100,180,240));draw=ImageDraw.Draw(source)
        for x in range(0,256,8):draw.line((x,0,x,511),fill='white',width=3)
        labels=Image.new('L',condition.size);ImageDraw.Draw(labels).rectangle((320,0,511,511),fill=11)
        crops=worker.detail_caption_regions(source,condition,labels)
        self.assertTrue(crops)
        self.assertLessEqual(len(crops),2)
    def test_cpu_install_is_detected_on_nvidia_machine(self):
        fake=types.SimpleNamespace(version=types.SimpleNamespace(cuda=None),cuda=types.SimpleNamespace(is_available=lambda:False))
        events=[]
        with patch.object(worker.importlib.metadata,'version',side_effect=lambda name:worker.VERSIONS[name]),patch.dict('sys.modules',torch=fake),patch.object(worker,'has_nvidia',return_value=True),patch.object(worker,'emit',side_effect=lambda event,**values:events.append(values)):
            worker.probe()
        self.assertIn('torch CUDA',events[-1]['missing'])
        self.assertFalse(events[-1]['cuda'])
    def test_bootstrap_repairs_partial_runtime_and_pip(self):
        python_files={'python.exe':b'python executable','python311.dll':b'python library','python311.zip':b'stdlib zip','_socket.pyd':b'socket extension','python311._pth':b'original paths'}
        pip_files={'pip/__init__.py':b'pip package','pip/__main__.py':b'pip launcher'}
        def packed(files):
            stream=io.BytesIO()
            with worker.zipfile.ZipFile(stream,'w') as archive:
                for name,content in files.items():archive.writestr(name,content)
            return stream.getvalue()
        with tempfile.TemporaryDirectory() as folder:
            runtime=Path(folder);python=runtime/'python';python.mkdir()
            executable=python/'python.exe';executable.write_bytes(python_files['python.exe']);original_time=executable.stat().st_mtime_ns
            (python/'python311.dll').write_bytes(b'x'*len(python_files['python311.dll']))
            site=python/'Lib/site-packages';(site/'pip').mkdir(parents=True)
            (site/'pip/__init__.py').write_bytes(b'interrupted pip')
            def downloaded(url,target,expected):
                self.assertEqual(expected,worker.PYTHON_SHA if url==worker.PYTHON_URL else worker.PIP_SHA)
                Path(target).write_bytes(packed(python_files if url==worker.PYTHON_URL else pip_files))
            with patch.object(worker,'download',side_effect=downloaded),patch.object(worker,'dependency_mirror',return_value=False),patch.object(worker,'pip') as install,patch.object(worker,'has_nvidia',return_value=False),patch.object(worker.importlib.metadata,'version',side_effect=lambda name:worker.VERSIONS[name]),patch.object(worker,'emit'):
                worker.bootstrap({'runtime':str(runtime),'backend':'cpu'})
                for name,content in python_files.items():
                    if name!='python311._pth':self.assertEqual((python/name).read_bytes(),content)
                self.assertEqual(executable.stat().st_mtime_ns,original_time)
                for name,content in pip_files.items():self.assertEqual((site/name).read_bytes(),content)
                self.assertIn('Lib/site-packages', (python/'python311._pth').read_text())
                self.assertIn('torch==2.8.0+cpu',install.call_args_list[0].args[1])
                # A completed runtime is validated without overwriting loaded files.
                with patch.object(worker.os,'replace',side_effect=AssertionError('valid component was rewritten')):
                    worker.bootstrap({'runtime':str(runtime),'backend':'cpu'})
    def test_bootstrap_retries_interrupted_atomic_runtime_repair(self):
        stream=io.BytesIO()
        with worker.zipfile.ZipFile(stream,'w') as archive:
            for name in ('python.exe','python311.dll','python311.zip'):archive.writestr(name,name.encode())
        content=stream.getvalue()
        with tempfile.TemporaryDirectory() as folder:
            runtime=Path(folder);python=runtime/'python';python.mkdir();(python/'python.exe').write_bytes(b'python.exe')
            def downloaded(url,target,expected):
                if url==worker.PYTHON_URL:Path(target).write_bytes(content)
                else:
                    with worker.zipfile.ZipFile(target,'w') as archive:archive.writestr('pip/__main__.py',b'pip')
            with patch.object(worker,'download',side_effect=downloaded),patch.object(worker,'dependency_mirror',return_value=False),patch.object(worker,'pip'),patch.object(worker,'has_nvidia',return_value=False),patch.object(worker.importlib.metadata,'version',side_effect=lambda name:worker.VERSIONS[name]),patch.object(worker,'emit'):
                with patch.object(worker.os,'replace',side_effect=OSError('interrupted extraction')):
                    with self.assertRaises(OSError):worker.bootstrap({'runtime':str(runtime)})
                self.assertTrue((python/'python.exe').exists())
                self.assertFalse((python/'python311.dll').exists())
                self.assertFalse(list(python.glob('*.repair.part')))
                worker.bootstrap({'runtime':str(runtime)})
                for name in ('python.exe','python311.dll','python311.zip'):self.assertEqual((python/name).read_bytes(),name.encode())
    def test_pixelated_redaction_is_guarded(self):
        from PIL import Image,ImageDraw
        import numpy as np
        image=Image.new('RGB',(400,600));draw=ImageDraw.Draw(image)
        for row in range(8):
            for col in range(8):
                draw.rectangle((100+col*20,200+row*20,119+col*20,219+row*20),fill=((row*23+col*11)%255,(col*31+row*7)%255,(row*17+col*13)%255))
        guarded=np.asarray(worker.pixelation_mask(image))
        self.assertTrue((guarded[200:360,100:260]==255).all())
        self.assertIsNone(worker.pixelation_mask(Image.new('RGB',(400,600),'grey')).getbbox())
    def test_palette_retains_original_chroma(self):
        from PIL import Image
        import numpy as np
        source=Image.new('RGB',(10,10),(170,140,90))
        candidate=Image.new('RGB',(10,10),(60,80,90))
        result=worker.matching_palette(candidate,source)
        chroma=np.asarray(result.convert('YCbCr'))[:,:,1:].astype(int)
        original=np.asarray(source.convert('YCbCr'))[:,:,1:].astype(int)
        self.assertLessEqual(abs(chroma-original).max(),2)
    def test_palette_limits_new_black_without_erasing_source_shadows(self):
        from PIL import Image
        import numpy as np
        source=Image.new('RGB',(2,1),(130,185,220));source.putpixel((1,0),(30,40,55))
        result=worker.matching_palette(Image.new('RGB',source.size,'black'),source,.38)
        luminance=np.asarray(result.convert('L'))[0]
        self.assertGreaterEqual(int(luminance[0]),94)
        self.assertLess(int(luminance[1]),50)
    def test_palette_saturated_colors_retain_target_luminance(self):
        from PIL import Image
        for color in ((0,0,255),(255,0,0),(0,255,255),(255,0,255)):
            source=Image.new('RGB',(1,1),color)
            for gray in (40,100,180,235):
                result=worker.matching_palette(Image.new('RGB',(1,1),(gray,gray,gray)),source)
                self.assertLessEqual(abs(result.convert('L').getpixel((0,0))-gray),1)
    def test_latent_refinement_leaves_inference_mode_and_restores_vae(self):
        import torch
        vae=types.SimpleNamespace(dtype=torch.float16)
        changes=[]
        vae.to=lambda **kwargs:changes.append(kwargs['dtype'])
        def failing(*args,**kwargs):
            self.assertFalse(torch.is_inference_mode_enabled())
            self.assertTrue(torch.is_grad_enabled())
            raise RuntimeError('interrupted refinement')
        with torch.inference_mode(),patch.object(worker,'_refine_latent',side_effect=failing):
            with self.assertRaisesRegex(RuntimeError,'interrupted refinement'):
                worker.refine_latent(types.SimpleNamespace(vae=vae),None,None)
        self.assertEqual(changes,[torch.float16])
    def test_reposition_keeps_full_code_and_quiet_zone(self):
        from PIL import Image,ImageDraw
        import numpy as np
        condition=Image.new('RGB',(576,576),(128,128,128));draw=ImageDraw.Draw(condition)
        # A 32px module grid deliberately starts at the edge, to exercise clamping.
        for row in range(5):
            for col in range(5):
                draw.rectangle((col*32,row*32,col*32+31,row*32+31),fill='white' if (row+col)%2 else 'black')
        result,bounds=worker.place_away_from_subject(condition,Image.new('L',condition.size))
        self.assertGreaterEqual(bounds['x'],128);self.assertGreaterEqual(bounds['y'],128)
        self.assertLessEqual(bounds['x']+160,448);self.assertLessEqual(bounds['y']+160,448)
        moved=np.asarray(result.crop((bounds['x'],bounds['y'],bounds['x']+160,bounds['y']+160)))
        self.assertTrue(np.array_equal(moved,np.asarray(condition.crop((0,0,160,160)))))
    def test_adaptive_placement_preserves_grid_and_clears_finders(self):
        from PIL import Image,ImageDraw
        import numpy as np
        code=Image.new('RGB',(336,336),'white');draw=ImageDraw.Draw(code)
        for x,y in ((0,0),(224,0),(0,224)):
            draw.rectangle((x,y,x+111,y+111),fill='black')
            draw.rectangle((x+16,y+16,x+95,y+95),fill='white')
            draw.rectangle((x+32,y+32,x+79,y+79),fill='black')
        condition=Image.new('RGB',(576,576),(128,128,128));condition.paste(code,(120,120))
        labels=Image.new('L',condition.size);ImageDraw.Draw(labels).rectangle((224,0,351,575),fill=2)
        placed,materials,region=worker.adaptive_placement(condition,labels,Image.new('RGB',condition.size,'blue'))
        self.assertGreater(placed.width,576);self.assertEqual(materials.size,placed.size)
        self.assertEqual(region['size'],336);self.assertLessEqual(region['finder_overlap'],.005)
        self.assertLessEqual(region['protected_overlap'],.12);self.assertLessEqual(max(placed.size),3072)
        x,y=region['x'],region['y']
        restored=placed.crop((x,y,x+336,y+336)).rotate(-region['rotation'])
        self.assertEqual(restored.tobytes(),code.tobytes())
    def test_condition_selection_matches_source_without_relocation(self):
        from PIL import Image,ImageDraw
        import numpy as np
        with tempfile.TemporaryDirectory() as root:
            choices=[]
            for reverse in (False,True):
                image=Image.new('RGB',(128,128),(128,128,128));draw=ImageDraw.Draw(image)
                for row in range(4):
                    for col in range(4):
                        draw.rectangle((32+col*16,32+row*16,47+col*16,47+row*16),fill='white' if bool((col+row)%2)^reverse else 'black')
                path=Path(root)/f'mask{reverse}.png';image.save(path);choices.append(path)
            source=Image.open(choices[1]).convert('RGB')
            result=worker.matching_condition(Image.open(choices[0]).convert('RGB'),source,choices)
            self.assertTrue(np.array_equal(np.asarray(result),np.asarray(source)))
    def test_blueprint_preserves_texture_and_separate_target(self):
        from PIL import Image,ImageDraw
        import numpy as np
        condition=Image.new('RGB',(400,400),(128,128,128));draw=ImageDraw.Draw(condition)
        for row in range(21):
            for col in range(21):
                draw.rectangle((32+col*16,32+row*16,47+col*16,47+row*16),fill='white' if (row+col)%2 else 'black')
        before=np.asarray(condition).copy()
        yy,xx=np.mgrid[:400,:400]
        texture=Image.fromarray((130+xx%13+yy%7).astype(np.uint8)).convert('RGB')
        guide=worker.material_blueprint(condition,texture)
        self.assertTrue(np.array_equal(before,np.asarray(condition)))
        self.assertTrue((np.asarray(guide)[:16]==128).all())
        self.assertGreater(len(np.unique(np.asarray(guide))),10)
    def test_finders_keep_a_light_separator_without_filling_the_data_area(self):
        import numpy as np
        mask=worker.finder_separators((400,400),32,32,21*16,16)
        self.assertTrue(mask[16:32,16:160].all())
        self.assertTrue(mask[32:144,144:160].all())
        self.assertFalse(mask[32:144,32:144].any())
        self.assertFalse(mask[176:208,176:208].any())
        self.assertFalse(mask[:16].any())
    def test_oriented_finders_and_separators_remain_at_the_correct_corners(self):
        from PIL import Image,ImageDraw
        import numpy as np
        code=Image.new('RGB',(21*16,21*16),'white');draw=ImageDraw.Draw(code)
        for x,y in ((0,0),(14*16,0),(0,14*16)):
            draw.rectangle((x,y,x+7*16-1,y+7*16-1),fill='black')
            draw.rectangle((x+16,y+16,x+6*16-1,y+6*16-1),fill='white')
            draw.rectangle((x+32,y+32,x+5*16-1,y+5*16-1),fill='black')
        for angle,missing in ((0,(14*16+32,14*16+32)),(90,(14*16+32,32)),(180,(32,32)),(270,(32,14*16+32))):
            image=Image.new('RGB',(400,400),(128,128,128));image.paste(code.rotate(angle),(32,32))
            grid=np.asarray(image)[:,:,0];boxes=worker.finder_boxes(grid,32,32,336,16)
            self.assertEqual(len(boxes),3);self.assertNotIn(missing,boxes)
            separator=worker.finder_separators(grid.shape,32,32,336,16,boxes)
            for x,y in boxes:self.assertTrue(separator[y-16:y,x:x+7*16].all())
    def test_resume_and_cached_hash(self):
        content=b'pinned model bytes';digest=hashlib.sha256(content).hexdigest()
        with tempfile.TemporaryDirectory() as root:
            target=Path(root)/'model';target.with_suffix('.part').write_bytes(content[:6])
            def request(req,timeout):
                self.assertEqual(req.get_header('Range'),'bytes=6-')
                return Response(content[6:],206,{'Content-Range':f'bytes 6-{len(content)-1}/{len(content)}'})
            with patch.object(worker.urllib.request,'urlopen',side_effect=request):worker.download('https://example.invalid/model',target,digest,len(content))
            self.assertEqual(target.read_bytes(),content)
            with patch.object(worker.urllib.request,'urlopen',side_effect=AssertionError('network for cached model')):worker.download('https://example.invalid/model',target,digest,len(content))
    def test_corrupt_content_not_promoted(self):
        with tempfile.TemporaryDirectory() as root:
            target=Path(root)/'model'
            with patch.object(worker.urllib.request,'urlopen',side_effect=lambda *a,**k:Response(b'bad')),patch.object(worker.time,'sleep'):
                with self.assertRaises(IOError):worker.download('https://example.invalid/model',target,'0'*64,3)
            self.assertFalse(target.exists());self.assertFalse(target.with_suffix('.part').exists())

if __name__=='__main__':unittest.main()
