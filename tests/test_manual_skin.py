"""Manual placement allows bounded limb-skin light without changing identity."""
from pathlib import Path
from contextlib import nullcontext
import importlib.util
import io
import tempfile
import types
import unittest
from unittest.mock import patch

import numpy as np
from PIL import Image

spec=importlib.util.spec_from_file_location('local_qr',Path(__file__).resolve().parents[1]/'ai/local_qr.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)


class ManualSkinTests(unittest.TestCase):
    def test_only_selected_limb_interiors_are_editable(self):
        labels=np.zeros((128,160),dtype=np.uint8)
        labels[16:112,20:140]=14
        labels[25:45,35:55]=11
        labels[25:45,65:85]=2
        labels[25:45,95:115]=3
        condition=np.full((128,160,3),128,dtype=np.uint8)
        condition[20:100,30:130]=255
        coverage=np.asarray(worker.manual_skin_mask(Image.fromarray(labels),Image.fromarray(condition)))
        selected=coverage>0
        self.assertTrue(selected.any())
        self.assertTrue(np.all(labels[selected]==14))
        self.assertEqual(int(coverage[:20].max()),0)
        self.assertEqual(int(coverage[:,130:].max()),0)
        for left in (35,65,95):
            self.assertEqual(int(coverage[20:50,left-5:left+25].max()),0)
        self.assertEqual(int(coverage[109:].max()),0)
        self.assertEqual(int(coverage[:,137:].max()),0)

    def test_clothes_face_hair_and_mosaic_remain_protected(self):
        for label in (2,3,4,7,11,17):
            labels=Image.new('L',(80,80),label)
            condition=Image.new('RGB',labels.size,'white')
            self.assertIsNone(worker.manual_skin_mask(labels,condition).getbbox())
        for label in (12,13,14,15):
            labels=Image.new('L',(80,80),label)
            self.assertIsNotNone(worker.manual_skin_mask(labels,condition).getbbox())

    def test_auto_editable_mask_remains_identical(self):
        labels=np.zeros((80,96),dtype=np.uint8)
        labels[:,32:64]=4;labels[20:60,70:90]=14;labels[10:30,10:30]=11
        image=Image.fromarray(labels)
        self.assertTrue(np.array_equal(np.asarray(worker.editable_material_mask(image)),np.asarray(worker.combined_material_mask(image))))

    def test_flesh_light_keeps_hue_bounds_detail_and_other_pixels(self):
        yy,xx=np.mgrid[:96,:128]
        texture=(xx+yy)%2*6
        rgb=np.stack((210+texture,158+texture,140+texture),axis=2).astype(np.uint8)
        source=Image.fromarray(rgb)
        generated=Image.new('RGB',source.size,(0,255,0))
        skin=np.zeros((96,128),dtype=np.uint8);skin[20:76,30:98]=255
        output=np.asarray(worker.constrained_skin_light(generated,source,Image.fromarray(skin)))
        self.assertTrue(np.array_equal(output[skin==0],np.asarray(generated)[skin==0]))
        source_y=np.asarray(source.convert('YCbCr'))[:,:,0].astype(int)
        output_y=np.asarray(Image.fromarray(output).convert('YCbCr'))[:,:,0].astype(int)
        self.assertLessEqual(int(np.abs(output_y[skin>0]-source_y[skin>0]).max()),64)
        # A green generated patch cannot change the original warm flesh hue.
        self.assertTrue(np.all(output[skin>0,0]>output[skin>0,1]))
        self.assertTrue(np.all(output[skin>0,1]>output[skin>0,2]))
        self.assertGreater(float(output_y[30:60,40:70].std()),2.)

    def test_soft_mask_does_not_replace_unselected_pixels(self):
        source=Image.new('RGB',(32,32),(210,160,140))
        image=Image.new('RGB',source.size,'white')
        mask=np.zeros((32,32),dtype=np.uint8);mask[8:24,8:24]=128
        result=worker.constrained_skin_light(image,source,Image.fromarray(mask))
        delta=np.asarray(result.convert('YCbCr'))[:,:,0].astype(int)-np.asarray(source.convert('YCbCr'))[:,:,0].astype(int)
        self.assertLessEqual(int(delta[8:24,8:24].max()),32)
        self.assertTrue(np.array_equal(np.asarray(result)[mask==0],np.asarray(image)[mask==0]))

    def test_bounded_flesh_colored_qr_scans_full_and_800(self):
        import zxingcpp
        payload='https://example.com/manual-skin?case=1#test'
        code=np.asarray(zxingcpp.write_barcode(zxingcpp.BarcodeFormat.QRCode,payload,width=960,height=960))
        condition=Image.fromarray(code).convert('RGB')
        yy,xx=np.mgrid[:condition.height,:condition.width]
        fine=(xx%4+yy%4)*2
        rgb=np.stack((204+fine,158+fine,140+fine),axis=2).astype(np.uint8)
        source=Image.fromarray(rgb)
        mask=worker.manual_skin_mask(Image.new('L',condition.size,14),condition)
        result=worker.constrained_skin_light(condition,source,mask)
        self.assertTrue(worker.independent_validation(result,payload))
        self.assertTrue(worker.independent_validation(result.resize((800,800),Image.Resampling.LANCZOS),payload))

    def test_quantized_final_y_bound_for_saturated_colors_and_all_integer_limits(self):
        colors=np.array([(0,0,0),(255,255,255),(255,0,0),(0,255,0),(0,0,255),
                         (255,255,0),(255,0,255),(0,255,255),(254,1,1),
                         (1,254,1),(1,1,254),(255,127,0),(210,160,140)],dtype=np.uint8)
        source=Image.fromarray(np.tile(colors[None],(12,1,1)))
        mask_values=np.tile(np.array([0,1,2,17,64,127,128,191,254,255,255,255],dtype=np.uint8)[:,None],(1,len(colors)))
        skin=Image.fromarray(mask_values)
        source_y=np.asarray(source.convert('YCbCr'))[:,:,0].astype(np.int16)
        for value in range(65):
            for extreme in ('black','white'):
                generated=Image.new('RGB',source.size,extreme)
                output=worker.constrained_skin_light(generated,source,skin,value)
                y=np.asarray(output.convert('YCbCr'))[:,:,0].astype(np.int16)
                maximum=value*mask_values.astype(np.float64)/255.
                self.assertTrue(np.all(np.abs(y-source_y)[mask_values>0]<=maximum[mask_values>0]),(value,extreme))
                self.assertTrue(np.array_equal(np.asarray(output)[mask_values==0],np.asarray(generated)[mask_values==0]))

    def test_quantized_final_y_bound_on_seeded_rgb_and_soft_mask_gradients(self):
        rng=np.random.default_rng(413)
        colors=rng.integers(0,256,size=(128,256,3),dtype=np.uint8)
        generated=Image.fromarray(rng.integers(0,256,size=colors.shape,dtype=np.uint8))
        source=Image.fromarray(colors)
        mask_values=np.tile(np.arange(256,dtype=np.uint8),(128,1))
        skin=Image.fromarray(mask_values)
        source_y=np.asarray(source.convert('YCbCr'))[:,:,0].astype(np.int16)
        for value in (0.,.1,.5,1.,7.25,32.,63.5,64.):
            output=worker.constrained_skin_light(generated,source,skin,value)
            y=np.asarray(output.convert('YCbCr'))[:,:,0].astype(np.int16)
            maximum=value*mask_values.astype(np.float64)/255.
            self.assertTrue(np.all(np.abs(y-source_y)[mask_values>0]<=maximum[mask_values>0]),value)
            self.assertTrue(np.array_equal(np.asarray(output)[mask_values==0],np.asarray(generated)[mask_values==0]))
            if value==0:
                self.assertTrue(np.array_equal(np.asarray(output)[mask_values>0],colors[mask_values>0]))

    def test_repeated_skin_finalization_stays_inside_the_original_bound(self):
        rng=np.random.default_rng(2026)
        source=Image.fromarray(rng.integers(0,256,size=(80,80,3),dtype=np.uint8))
        image=Image.new('RGB',source.size,'black')
        skin=Image.new('L',source.size,255)
        source_y=np.asarray(source.convert('YCbCr'))[:,:,0].astype(np.int16)
        for _ in range(5):
            image=worker.constrained_skin_light(image,source,skin,64)
            y=np.asarray(image.convert('YCbCr'))[:,:,0].astype(np.int16)
            self.assertLessEqual(int(np.abs(y-source_y).max()),64)

    def test_mismatched_dimensions_are_rejected(self):
        image=Image.new('RGB',(32,32))
        with self.assertRaisesRegex(ValueError,'matching dimensions'):
            worker.manual_skin_mask(Image.new('L',(31,32)),image)
        with self.assertRaisesRegex(ValueError,'matching dimensions'):
            worker.constrained_skin_light(image,image,Image.new('L',(31,32)))

    def test_skin_prompt_does_not_turn_skin_into_captioned_clothes(self):
        prompt=worker.local_skin_prompt('anime woman wearing a leopard knit sweater in a city')
        self.assertIn('illustration',prompt)
        self.assertIn('original skin',prompt)
        self.assertIn('unchanged anatomy',prompt)
        for invented in ('woman','leopard','sweater','city','knit','shirt'):
            self.assertNotIn(invented,prompt)

    def test_error_protocol_distinguishes_scan_exhaustion_only(self):
        with patch.object(worker,'emit') as emit:
            worker.report_error(worker.QrScanFailed('Scan exhausted'))
            self.assertEqual(emit.call_args.kwargs['code'],'scan_failed')
            worker.report_error(RuntimeError('CUDA failure'))
            self.assertEqual(emit.call_args.kwargs['code'],'failed')
            worker.report_error(ValueError('wrong input'))
            self.assertEqual(emit.call_args.kwargs['code'],'failed')

    def run_generate(self, request_options, accept=True, pipe_failure=None, labels=None):
        calls=[];events=[]
        fake_torch=types.ModuleType('torch')
        fake_torch.cuda=types.SimpleNamespace(is_available=lambda:False)
        fake_torch.float32='float32';fake_torch.float16='float16'
        fake_torch.OutOfMemoryError=type('OutOfMemoryError',(RuntimeError,),{})
        fake_torch.set_num_threads=lambda count:None
        fake_torch.inference_mode=nullcontext
        fake_torch.Generator=lambda **kwargs:types.SimpleNamespace(manual_seed=lambda seed:seed)
        class FakePipe:
            def __init__(self):self.scheduler=types.SimpleNamespace(config={})
            @classmethod
            def from_pretrained(cls,*args,**kwargs):return cls()
            from_single_file=from_pretrained
            def enable_vae_slicing(self):pass
            def enable_vae_tiling(self):pass
            def to(self,device):pass
            def __call__(self,**kwargs):
                calls.append(kwargs)
                if pipe_failure=='oom':raise fake_torch.OutOfMemoryError('Insufficient memory')
                if pipe_failure is not None:raise pipe_failure
                return types.SimpleNamespace(images=[Image.new('RGB',kwargs['image'].size,(40,220,40))])
        fake_diffusers=types.ModuleType('diffusers')
        for name in ('ControlNetModel','StableDiffusionControlNetPipeline','StableDiffusionControlNetImg2ImgPipeline','StableDiffusionXLControlNetPipeline','StableDiffusionXLControlNetImg2ImgPipeline'):
            setattr(fake_diffusers,name,FakePipe)
        fake_diffusers.DPMSolverMultistepScheduler=types.SimpleNamespace(from_config=lambda *args,**kwargs:types.SimpleNamespace(config={}))
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);(root/'base').mkdir();(root/'control').mkdir()
            source=Image.new('RGB',(128,128),(210,160,140));source.save(root/'source.png')
            array=np.full((128,128,3),128,dtype=np.uint8);array[24:104,24:104]=255
            Image.fromarray(array).save(root/'condition.png')
            label_image=labels or Image.new('L',source.size,14)
            request=dict(operation='generate',models=str(root),base=str(root/'base'),control=str(root/'control'),input=str(root/'source.png'),condition=str(root/'condition.png'),output=str(root/'result.png'),family='sd15',prompt='original material',auto_prompt=False,attempts=1,seed=1,local_patch=False)
            request.update(request_options)
            with patch.dict('sys.modules',{'torch':fake_torch,'diffusers':fake_diffusers}),patch.object(worker,'parse_materials',return_value=label_image),patch.object(worker,'face_guards',return_value=Image.new('L',source.size)),patch.object(worker,'pixelation_mask',return_value=Image.new('L',source.size)),patch.object(worker,'package_path',side_effect=lambda root,path:Path(root)/path),patch.object(worker,'emit',side_effect=lambda event,**values:events.append((event,values))),patch.object(worker.sys,'stdin',io.StringIO('{"accept":'+str(accept).lower()+'}\n')),patch.object(worker,'adaptive_placement',side_effect=lambda condition,materials,source:(condition,materials,{'rotation':0})) as placement:
                worker.generate(request)
                output=Image.open(root/'result.png').convert('RGB').copy()
                return calls,events,output,source,placement.call_count

    def test_actual_manual_route_keeps_position_and_skin_pose(self):
        calls,events,output,source,placements=self.run_generate({'auto_place':False,'manual_skin':True})
        self.assertEqual(placements,0)
        self.assertEqual(len(calls),1)
        self.assertIn('original skin surface',calls[0]['prompt'])
        self.assertEqual(calls[0]['strength'],.28)
        self.assertNotIn(', skin,',calls[0]['negative_prompt'])
        result=np.asarray(output);original=np.asarray(source)
        self.assertTrue(np.array_equal(result[:24],original[:24]))
        self.assertTrue(np.array_equal(result[104:],original[104:]))
        self.assertFalse(np.array_equal(result[40:80,40:80],original[40:80,40:80]))
        self.assertTrue(np.all(result[40:80,40:80,0]>result[40:80,40:80,1]))
        self.assertIsNone(next(values for event,values in events if event=='result')['region'])

    def test_default_and_auto_routes_keep_all_skin_protected(self):
        for options,placements in (({'auto_place':False},0),({'auto_place':True,'manual_skin':True},1),({'auto_place':True,'manual_skin':False},1)):
            calls,events,output,source,actual=self.run_generate(options)
            self.assertEqual(actual,placements)
            self.assertEqual(len(calls),0)
            self.assertTrue(np.array_equal(np.asarray(output),np.asarray(source)))

    def test_auto_material_prompt_and_strength_are_unchanged(self):
        calls,events,output,source,placements=self.run_generate({'auto_place':True,'manual_skin':True},labels=Image.new('L',(128,128),4))
        self.assertEqual(len(calls),1)
        self.assertEqual(calls[0]['prompt'],worker.local_material_prompt('original material',True))
        self.assertEqual(calls[0]['strength'],.42)
        self.assertIn(', skin, body parts,',calls[0]['negative_prompt'])

    def test_manual_generation_restores_all_identity_pixels(self):
        labels=np.full((128,128),14,dtype=np.uint8)
        labels[40:55,40:55]=11;labels[65:80,40:55]=3;labels[40:55,70:85]=2
        calls,events,output,source,placements=self.run_generate({'auto_place':False,'manual_skin':True},labels=Image.fromarray(labels))
        mask=np.isin(labels,(2,3,11))
        self.assertTrue(np.array_equal(np.asarray(output)[mask],np.asarray(source)[mask]))

    def test_actual_exhaustion_is_typed_but_pipeline_errors_are_not(self):
        with self.assertRaises(worker.QrScanFailed):
            self.run_generate({'auto_place':False,'manual_skin':True},accept=False)
        with self.assertRaisesRegex(RuntimeError,'Model loading problem') as error:
            self.run_generate({'auto_place':False,'manual_skin':True},pipe_failure=RuntimeError('Model loading problem'))
        self.assertNotIsInstance(error.exception,worker.QrScanFailed)
        with self.assertRaisesRegex(RuntimeError,'Insufficient memory') as error:
            self.run_generate({'auto_place':False,'manual_skin':True},pipe_failure='oom')
        self.assertNotIsInstance(error.exception,worker.QrScanFailed)


if __name__=='__main__':unittest.main()
