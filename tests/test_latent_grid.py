"""CPU regressions for exact QR modules during latent-refinement reduction."""
from pathlib import Path
import importlib.util
import math
import types
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('local_qr',Path(__file__).resolve().parents[1]/'ai/local_qr.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)


class LatentGridTests(unittest.TestCase):
    def condition(self,size=1088,module=16,offset=None):
        import numpy as np
        import zxingcpp
        from PIL import Image
        matrix=np.asarray(zxingcpp.write_barcode(zxingcpp.BarcodeFormat.QRCode,
            'https://example.com/test?data='+'x'*50,quiet_zone=0))
        self.assertEqual(matrix.shape,(37,37))
        code=Image.fromarray(matrix).resize((37*module,37*module),Image.Resampling.NEAREST).convert('RGB')
        condition=Image.new('RGB',(size,size),(128,128,128))
        offset=(size-code.width)//2 if offset is None else offset
        condition.paste(code,(offset,offset))
        return condition,matrix

    def measured_grid(self,condition):
        import numpy as np
        grid=np.asarray(condition)[:,:,0];yy,xx=np.where(grid!=128)
        left,top,right,bottom=int(xx.min()),int(yy.min()),int(xx.max())+1,int(yy.max())+1
        side=right-left;module=0
        for line in grid[top:bottom,left:right]:
            for run in np.diff(np.r_[0,np.flatnonzero(np.diff(line.astype(np.int16)))+1,side]):module=math.gcd(module,int(run))
        samples=grid[top+module//2:bottom:module,left+module//2:right:module]
        return module,samples,worker.finder_boxes(grid,left,top,side,module)

    def test_1088_patch_uses_four_not_three_and_preserves_all_37_modules(self):
        import numpy as np
        from PIL import Image
        condition,matrix=self.condition()
        self.assertEqual(worker.latent_refinement_divisor(condition),4)
        # The previous ceil(1088/512)=3 path really corrupts the run GCD.
        broken=condition.resize((1088//3,1088//3),Image.Resampling.NEAREST)
        self.assertEqual(self.measured_grid(broken)[0],1)
        reduced=condition.resize((272,272),Image.Resampling.NEAREST)
        module,samples,finders=self.measured_grid(reduced)
        self.assertEqual(module,4);self.assertEqual(samples.shape,(37,37))
        self.assertTrue(np.array_equal(samples,matrix));self.assertEqual(len(finders),3)

    def fake_vae(self):
        import torch
        import torch.nn.functional as F
        vae=types.SimpleNamespace(device=torch.device('cpu'),dtype=torch.float32,
            config=types.SimpleNamespace(scaling_factor=1),input_sizes=[])
        vae.to=lambda **kwargs:vae
        vae.parameters=lambda:[]
        def encode(value):
            vae.input_sizes.append(tuple(value.shape[-2:]))
            reduced=F.avg_pool2d(value,8)
            return types.SimpleNamespace(latent_dist=types.SimpleNamespace(mode=lambda:reduced))
        vae.encode=encode
        vae.decode=lambda value,return_dict=False:(F.interpolate(value,scale_factor=8,mode='nearest'),)
        return vae

    def test_1088_refinement_actually_samples_a_37_by_37_grid_on_cpu(self):
        import torch.nn.functional as F
        from PIL import Image
        condition,_=self.condition();vae=self.fake_vae()
        source=Image.new('RGB',condition.size,(140,150,160))
        with patch.object(worker,'emit'),patch.object(F,'grid_sample',wraps=F.grid_sample) as sample:
            result=worker._refine_latent(types.SimpleNamespace(vae=vae),source,condition,steps=1)
        self.assertEqual(vae.input_sizes,[(272,272)])
        self.assertEqual(tuple(sample.call_args.args[1].shape),(1,37,37,2))
        self.assertEqual(result.size,source.size)

    def test_1024_patch_retains_the_previous_exact_half_scale(self):
        import numpy as np
        import torch.nn.functional as F
        from PIL import Image
        condition,matrix=self.condition(size=1024);vae=self.fake_vae()
        self.assertEqual(worker.latent_refinement_divisor(condition),2)
        reduced=condition.resize((512,512),Image.Resampling.NEAREST)
        module,samples,finders=self.measured_grid(reduced)
        self.assertEqual(module,8);self.assertTrue(np.array_equal(samples,matrix));self.assertEqual(len(finders),3)
        source=Image.new('RGB',condition.size,(140,150,160))
        with patch.object(worker,'emit'),patch.object(F,'grid_sample',wraps=F.grid_sample) as sample:
            result=worker._refine_latent(types.SimpleNamespace(vae=vae),source,condition,steps=1)
        self.assertEqual(vae.input_sizes,[(512,512)])
        self.assertEqual(tuple(sample.call_args.args[1].shape),(1,37,37,2))
        self.assertEqual(result.size,source.size)

    def test_unaligned_bounds_skip_before_accessing_vae(self):
        from PIL import Image
        condition,_=self.condition(offset=249)
        self.assertIsNone(worker.latent_refinement_divisor(condition))
        source=Image.new('RGB',condition.size,'gray')
        with patch.object(worker,'emit'):
            self.assertIs(worker._refine_latent(None,source,condition,steps=1),source)

    def test_small_aligned_patch_needs_no_reduction(self):
        condition,_=self.condition(size=512,module=8)
        self.assertEqual(worker.latent_refinement_divisor(condition),1)

    def test_vae_dimensions_and_mismatched_canvas_skip_safely(self):
        from PIL import Image
        # Although divisor=2 aligns the QR, 1026/2 is not a VAE multiple of 8.
        condition,_=self.condition(size=1026,offset=216)
        self.assertIsNone(worker.latent_refinement_divisor(condition))
        condition,_=self.condition()
        source=Image.new('RGB',(1024,1024),'gray')
        with patch.object(worker,'emit'):
            self.assertIs(worker._refine_latent(None,source,condition,steps=1),source)


if __name__=='__main__':unittest.main()
