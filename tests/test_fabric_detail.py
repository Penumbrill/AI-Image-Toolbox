"""Fine source texture must not alter protected or untouched source pixels."""
from pathlib import Path
import importlib.util
import unittest

spec=importlib.util.spec_from_file_location('local_qr',Path(__file__).resolve().parents[1]/'ai/local_qr.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)


class FabricDetailTests(unittest.TestCase):
    def test_weave_returns_only_to_edited_clothing(self):
        import numpy as np
        from PIL import Image
        yy,xx=np.mgrid[:96,:128]
        weave=((xx//2+yy//2)%2)*16
        rgb=np.stack((170+weave,150+weave,130+weave),axis=2).astype(np.uint8)
        source=Image.fromarray(rgb)
        generated=rgb.copy();generated[20:76,20:108]=(120,140,160)
        labels=np.zeros((96,128),dtype=np.uint8);labels[:,32:96]=7
        protected=np.zeros_like(labels);protected[40:52,60:72]=255
        result=np.asarray(worker.restore_fabric_detail(Image.fromarray(generated),source,Image.fromarray(labels),Image.fromarray(protected)))
        self.assertTrue(np.array_equal(result[protected>0],rgb[protected>0]))
        self.assertTrue(np.array_equal(result[labels==0],generated[labels==0]))
        untouched=(labels==7)&np.all(generated==rgb,axis=2)
        self.assertTrue(np.array_equal(result[untouched],generated[untouched]))
        self.assertGreater(float(result[24:36,36:52,0].std()),2)

    def test_textured_colored_qr_remains_exactly_readable(self):
        import numpy as np
        import zxingcpp
        from PIL import Image
        payload='https://example.com/fabric?test=1#card'
        code=np.asarray(zxingcpp.write_barcode(zxingcpp.BarcodeFormat.QRCode,payload,width=640,height=640))
        if code.ndim==3:code=code[:,:,0]
        yy,xx=np.mgrid[:code.shape[0],:code.shape[1]]
        source=np.stack((185+xx%4*3,165+yy%4*3,145+xx%4*3),axis=2).astype(np.uint8)
        ink=code<128
        generated=np.where(ink[:,:,None],np.array((60,80,100),dtype=np.uint8),np.array((210,220,230),dtype=np.uint8))
        image=Image.fromarray(generated.astype(np.uint8))
        result=worker.restore_fabric_detail(image,Image.fromarray(source),Image.new('L',image.size,7))
        self.assertTrue(worker.independent_validation(result,payload))

    def test_mismatched_canvas_is_rejected(self):
        from PIL import Image
        image=Image.new('RGB',(64,64))
        with self.assertRaisesRegex(ValueError,'matching dimensions'):
            worker.restore_fabric_detail(image,image,Image.new('L',(63,64)))


if __name__=='__main__':unittest.main()
