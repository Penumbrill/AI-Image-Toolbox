"""Source preservation when removing the locator's rectangular backing plate."""
from pathlib import Path
import importlib.util
import unittest

spec=importlib.util.spec_from_file_location('local_qr',Path(__file__).resolve().parents[1]/'ai/local_qr.py')
worker=importlib.util.module_from_spec(spec);spec.loader.exec_module(worker)


class LocatorBackgroundTests(unittest.TestCase):
    def test_source_corners_and_protected_pixels_survive_plate_removal(self):
        import numpy as np
        import zxingcpp
        from PIL import Image,ImageDraw
        matrix=np.asarray(zxingcpp.write_barcode(zxingcpp.BarcodeFormat.QRCode,
            'https://example.com/test?data='+'x'*50,quiet_zone=0))
        condition=Image.new('RGB',(512,512),(128,128,128));module=8;offset=108
        condition.paste(Image.fromarray(matrix).resize((296,296),Image.Resampling.NEAREST).convert('RGB'),(offset,offset))
        yy,xx=np.mgrid[:512,:512]
        rgb=np.stack((150+xx%35,170+yy%30,200+xx%25),axis=2).astype(np.uint8)
        source=Image.fromarray(rgb);generated=source.copy()
        ImageDraw.Draw(generated).rectangle((offset-module,offset-module,offset+8*module-1,offset+8*module-1),fill=(230,235,240))
        protected=Image.new('L',source.size)
        ImageDraw.Draw(protected).rectangle((offset+2*module,offset+2*module,offset+4*module,offset+4*module),fill=255)
        result=worker.transparent_locator_background(generated,condition,source,protected)
        actual=np.asarray(result);mask=np.asarray(protected)>0
        self.assertTrue(np.array_equal(actual[mask],rgb[mask]))
        # Square corners have no recognition samples and reveal original texture.
        self.assertTrue(np.array_equal(actual[offset-module:offset-module+3,offset-module:offset-module+3],
                                       rgb[offset-module:offset-module+3,offset-module:offset-module+3]))
        self.assertTrue(np.array_equal(actual[:90,:90],np.asarray(generated)[:90,:90]))
        self.assertFalse(np.any(np.all(actual==0,axis=2)))

    def test_mismatched_reference_is_rejected_before_editing(self):
        from PIL import Image
        image=Image.new('RGB',(512,512),'white')
        with self.assertRaisesRegex(ValueError,'canvas sizes'):
            worker.transparent_locator_background(image,image,Image.new('RGB',(256,256),'white'))


if __name__=='__main__':unittest.main()
