using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace QQImageSwitch
{
    static class QrAiTests
    {
        static void Require(bool okay,string message){if(!okay)throw new Exception("Local AI: "+message);}
        public static void Run(string root)
        {
            QrAiIntegrityTests.Run(root);
            QrAiLocationsTests.Run(root);
            QrAiDirectoryProfileTests.Run(root);
            QrAiSchemeTests.Run(root);
            bool priorLanguage=L.English;bool mainland=QrAiService.MainlandRegion();try{foreach(bool english in new[]{false,true}){L.Set(english?"en":"zh");Require(QrAiService.MainlandRegion()==mainland,"interface language changed regional source preference");var progress=new System.Collections.Generic.Dictionary<string,object>{{"file","base/model.safetensors"},{"bytes",1048576L},{"total",2097152L},{"speed",524288.0},{"phase","downloading"}};string line=QrAiService.DownloadProgress(progress);Require(line.Contains("1.0 MiB / 2.0 MiB")&&line.Contains("512.0 KiB/s")&&line.Contains("50.0%"),"readable size/speed/percent missing");progress["bytes"]=2097152L;Require(!QrAiService.DownloadProgress(progress).Contains("100.0%"),"unverified transfer displayed completed percentage");progress["total"]=null;Require(QrAiService.DownloadProgress(progress).Contains(L.T("总大小未知"))&&!QrAiService.DownloadProgress(progress).Contains("%"),"unknown download total displayed percentage");progress["phase"]="verifying";Require(QrAiService.DownloadProgress(progress).Contains(L.T("校验中")),"hash verification status missing");}}finally{L.Set(priorLanguage?"en":"zh");}
            string dir=Path.Combine(root,"ai-model-discovery");Directory.CreateDirectory(dir);
            string control=Path.Combine(dir,"controlnet","custom-xl"),baseModel=Path.Combine(dir,"base","custom-xl");Directory.CreateDirectory(control);Directory.CreateDirectory(baseModel);
            File.WriteAllText(Path.Combine(control,"config.json"),"{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":2048,\"in_channels\":4}");
            File.WriteAllText(Path.Combine(baseModel,"model_index.json"),"{\"_class_name\":\"StableDiffusionXLPipeline\"}");
            Require(LocalModels(dir).Count==0,"incomplete downloads appear as ready models");
            Tensor(Path.Combine(control,"model.safetensors"),"controlnet_cond_embedding.conv_in.weight",2048);
            foreach(string component in new[]{"unet","vae","text_encoder","text_encoder_2"})
            {string folder=Path.Combine(baseModel,component);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"config.json"),QrAiDirectoryProfileTests.ComponentConfig(component,2048));Tensor(Path.Combine(folder,"model.safetensors"),"weight",1);}
            foreach(string component in new[]{"tokenizer","tokenizer_2"})
            {string folder=Path.Combine(baseModel,component);Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"tokenizer_config.json"),"{\"tokenizer_class\":\"CLIPTokenizer\"}");File.WriteAllText(Path.Combine(folder,"vocab.json"),"{\"test\":0}");File.WriteAllText(Path.Combine(folder,"merges.txt"),"#version: 0.2\n");}
            Directory.CreateDirectory(Path.Combine(baseModel,"scheduler"));File.WriteAllText(Path.Combine(baseModel,"scheduler","scheduler_config.json"),"{\"_class_name\":\"DDIMScheduler\"}");
            var models=LocalModels(dir);Require(models.Count==2&&models.All(m=>m.Family=="sdxl"),"directory families mismatch");
            File.WriteAllText(Path.Combine(control,"config.json"),"{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":1024,\"in_channels\":4}");Require(!LocalModels(dir).Any(m=>m.Path==control),"unsupported ControlNet dimensions mislabeled as SD 1.5");File.WriteAllText(Path.Combine(control,"config.json"),"{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":2048,\"in_channels\":4}");
            string tinyBase=Path.Combine(dir,"base","tiny-complete-base.safetensors");File.Copy(QrAiIntegrityTests.Model("tiny-base-profile",768,true,false,false),tinyBase,true);
            Require(QrAiIntegrity.Inspect(tinyBase).Role=="base"&&new FileInfo(tinyBase).Length<100000000,"small complete structural fixture is invalid");
            Require(!LocalModels(dir).Any(m=>m.Path==tinyBase),"automatic single-base scan ignored 100MB discovery minimum");
            string loneUnet=Path.Combine(dir,"controlnet","unet-component.safetensors");File.Copy(QrAiIntegrityTests.Model("unet-only-discovery",2048,false,false,false),loneUnet,true);
            Require(!LocalModels(dir).Any(m=>m.Path==loneUnet),"UNet component mistaken for standalone model");
            var hardware=new QrAiHardware{cuda=true,total_gb=24,free_gb=20};
            Require(QrAiService.UseScanningGuidance(hardware,"sd15","auto"),"high-memory SD15 guidance disabled");
            Require(!QrAiService.UseScanningGuidance(hardware,"sdxl","auto")&&!QrAiService.UseScanningGuidance(hardware,"sd15","low"),"unsupported guidance enabled");
            hardware.free_gb=3;Require(!QrAiService.UseScanningGuidance(hardware,"sd15","auto"),"busy GPU gradient guidance enabled");
            hardware.free_gb=20;hardware.cuda=false;Require(!QrAiService.UseScanningGuidance(hardware,"sd15","auto"),"CPU gradient guidance enabled");
            string loose=Path.Combine(dir,"controlnet","loose-xl.safetensors");File.Copy(QrAiIntegrityTests.Model("loose-xl-profile",2048,false,false,true),loose,true);
            Require(LocalModels(dir).Any(m=>m.Path==loose&&m.Family=="sdxl"&&m.Control),"SDXL complete single-file control inferred as SD 1.5");
            Tensor(Path.Combine(dir,"controlnet","adapter.safetensors"),"lora_unet_down",768);Require(!LocalModels(dir).Any(m=>m.Name.Contains("adapter")),"LoRA mistaken for ControlNet");
            string url="https://www.pixiv.net/artworks/123456789";QrRegion region;
            Require(!new QrAiOptions().AutoPlace,"AI defaults to relocating the manually chosen region");
            using(var condition=QrAiCondition.Create(url,new Size(2560,3840),512,60,45,70,out region))
            {Require(region.Size>=16*29&&Math.Max(condition.Width,condition.Height)>512,"automatic output has insufficient module pixels");Require(region.X>=0&&region.Y>=0&&region.X+region.Size<=condition.Width&&region.Y+region.Size<=condition.Height,"placement outside canvas");}
            using(var code=QrService.Code(url,1024)){Require(QrAiCondition.Validate(code,url),"valid output rejected");Require(!QrAiCondition.Validate(code,"https://example.com"),"wrong decoded URL accepted");}
            for(int mask=0;mask<8;mask++)using(var condition=QrAiCondition.Create(url,new Size(640,960),512,60,50,70,out region,mask))
            {Require(QrService.Scan(condition)==url,"standard mask changed decoded URL: "+mask);}
            foreach(var image in new[]{new Size(640,960),new Size(960,640),new Size(618,915),new Size(915,618),new Size(640,640),new Size(640,2560),new Size(2560,640)})
            foreach(int location in new[]{0,100})using(var condition=QrAiCondition.Create(url,image,512,90,location,100-location,out region))
            {
                int modules=new ZXing.QrCode.QRCodeWriter().encode(url,ZXing.BarcodeFormat.QR_CODE,1,1,new System.Collections.Generic.Dictionary<ZXing.EncodeHintType,object>{{ZXing.EncodeHintType.ERROR_CORRECTION,ZXing.QrCode.Internal.ErrorCorrectionLevel.H},{ZXing.EncodeHintType.CHARACTER_SET,"UTF-8"},{ZXing.EncodeHintType.MARGIN,0},{ZXing.EncodeHintType.DISABLE_ECI,true}}).Width;
                int scale=region.Size/modules,margin=scale*4;
                var layout=QrAiCondition.Layout(url,image,512,90,location,100-location);
                Require(layout.X==region.X&&layout.Y==region.Y&&layout.Size==region.Size&&layout.Width==region.Width&&layout.Height==region.Height,"preview and inference layout differ");
                using(var demo=new Bitmap(image.Width,image.Height)){using(var g=Graphics.FromImage(demo))g.Clear(Color.CornflowerBlue);var bounds=QrAiCondition.PreviewBounds(region,image,demo.Size);QrAiCondition.DrawPreview(demo,url,bounds);Require(QrService.Scan(demo)==url,"ordinary QR preview unreadable at aspect/boundary: "+image);}
                Require(scale>=16&&region.Size%modules==0,"rounded output has insufficient or uneven module pixels");
                Require(condition.Width%64==0&&condition.Height%64==0,"output dimensions not aligned to inference tiles");
                Require(region.X>=margin&&region.Y>=margin&&region.X+region.Size<=condition.Width-margin&&region.Y+region.Size<=condition.Height-margin,"quiet zone clipped at canvas edge: "+image);
                Require((image.Width>=image.Height?condition.Width:condition.Height)==Math.Max(condition.Width,condition.Height),"base image orientation changed");
                Require(QrService.Scan(condition)==url,"rounded output changed decoded URL: "+image);
            }
            bool longAspectRejected=false;try{using(var condition=QrAiCondition.Create(url,new Size(400,2000),512,90,50,50,out region)){} }catch(ArgumentException){longAspectRejected=true;}
            Require(longAspectRejected,"extreme aspect accepted with implicit crop");
            File.WriteAllText(Path.Combine(dir,"PASS.md"),"PASS: incomplete models, SDXL/SD15 discovery, LoRA rejection, all eight standard QR masks, output module clarity, quiet-zone bounds after 64-pixel rounding, aspect rejection and actual URL validation.");
        }
        static System.Collections.Generic.List<QrAiModel> LocalModels(string dir)
        {string prefix=Path.GetFullPath(dir)+Path.DirectorySeparatorChar;return QrAiService.Discover(dir).Where(m=>Path.GetFullPath(m.Path).StartsWith(prefix,StringComparison.OrdinalIgnoreCase)).ToList();}
        static void Tensor(string path,string key,int channels){int bytes=checked(16*channels);byte[] header=Encoding.UTF8.GetBytes("{\""+key+"\":{\"dtype\":\"F32\",\"shape\":[4,"+channels+"],\"data_offsets\":[0,"+bytes+"]}}");using(var output=new BinaryWriter(File.Create(path))){output.Write((ulong)header.Length);output.Write(header);output.Write(new byte[bytes]);}}
    }
}
