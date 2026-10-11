using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace QQImageSwitch
{
    static class QrAiDirectoryProfileTests
    {
        static int checks;
        static void Require(bool okay,string message){checks++;if(!okay)throw new Exception("Directory model profiles: "+message);}
        static void Tensor(string path)
        {
            byte[] header=Encoding.UTF8.GetBytes("{\"weight\":{\"dtype\":\"F32\",\"shape\":[1],\"data_offsets\":[0,4]}}");
            using(var output=new BinaryWriter(File.Create(path))){output.Write((ulong)header.Length);output.Write(header);output.Write(new byte[4]);}
        }
        internal static string ComponentConfig(string component,int context)
        {
            if(component=="unet")return "{\"_class_name\":\"UNet2DConditionModel\",\"cross_attention_dim\":"+context+",\"in_channels\":4,\"out_channels\":4}";
            if(component=="vae")return "{\"_class_name\":\"AutoencoderKL\",\"latent_channels\":4}";
            return "{\"model_type\":\"clip_text_model\",\"hidden_size\":"+(component=="text_encoder_2"?1280:768)+"}";
        }
        internal static void CreateBase(string dir,bool xl,string kind=null)
        {
            Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"model_index.json"),"{\"_class_name\":\""+(kind??(xl?"StableDiffusionXLPipeline":"StableDiffusionPipeline"))+"\"}");
            foreach(string component in xl?new[]{"unet","vae","text_encoder","text_encoder_2"}:new[]{"unet","vae","text_encoder"})
            {string child=Path.Combine(dir,component);Directory.CreateDirectory(child);File.WriteAllText(Path.Combine(child,"config.json"),ComponentConfig(component,xl?2048:768));Tensor(Path.Combine(child,"model.safetensors"));}
            string scheduler=Path.Combine(dir,"scheduler");Directory.CreateDirectory(scheduler);File.WriteAllText(Path.Combine(scheduler,"scheduler_config.json"),"{\"_class_name\":\"DDIMScheduler\"}");
            foreach(string component in xl?new[]{"tokenizer","tokenizer_2"}:new[]{"tokenizer"})
            {string child=Path.Combine(dir,component);Directory.CreateDirectory(child);File.WriteAllText(Path.Combine(child,"tokenizer_config.json"),"{\"tokenizer_class\":\"CLIPTokenizer\"}");File.WriteAllText(Path.Combine(child,"vocab.json"),"{\"test\":0}");File.WriteAllText(Path.Combine(child,"merges.txt"),"#version: 0.2\n");}
        }
        static void Check(string dir,bool control,string family)
        {
            var model=QrAiService.ReadModel(dir,control);Require(family==null?model==null:model!=null&&model.Family==family&&model.Control==control,Path.GetFileName(dir)+" explicit selection classification");
        }
        public static void Run(string root)
        {
            checks=0;string dir=Path.Combine(root,"directory-profile-tests");Directory.CreateDirectory(dir);
            foreach(bool xl in new[]{false,true})foreach(bool img2img in new[]{false,true})
            {string path=Path.Combine(dir,"base","valid-"+(xl?"xl":"sd15")+(img2img?"-img2img":""));CreateBase(path,xl,img2img?(xl?"StableDiffusionXLImg2ImgPipeline":"StableDiffusionImg2ImgPipeline"):null);Check(path,false,xl?"sdxl":"sd15");}
            string[] badUnets={
                "{\"_class_name\":\"UNet2DConditionModel\",\"cross_attention_dim\":1024,\"in_channels\":4,\"out_channels\":4}",
                "{\"_class_name\":\"UNet2DConditionModel\",\"cross_attention_dim\":768,\"in_channels\":9,\"out_channels\":4}",
                "{\"_class_name\":\"UNet2DConditionModel\",\"cross_attention_dim\":2048,\"in_channels\":4,\"out_channels\":4}",
                "{}", "{bad", "null",
                "{\"_class_name\":\"UNet2DConditionModel\",\"in_channels\":4,\"out_channels\":4}",
                "{\"_class_name\":\"UNet2DConditionModel\",\"cross_attention_dim\":\"768\",\"in_channels\":4,\"out_channels\":4}",
                "{\"_class_name\":\"UNet2DConditionModel\",\"cross_attention_dim\":[768],\"in_channels\":4,\"out_channels\":4}",
                "{\"_class_name\":\"UNet2DConditionModel\",\"cross_attention_dim\":768,\"in_channels\":4,\"out_channels\":9}",
                "{\"_class_name\":\"OtherUNet\",\"cross_attention_dim\":768,\"in_channels\":4,\"out_channels\":4}"
            };
            for(int i=0;i<badUnets.Length;i++){string path=Path.Combine(dir,"base","bad-unet-"+i);CreateBase(path,false);File.WriteAllText(Path.Combine(path,"unet","config.json"),badUnets[i]);Check(path,false,null);}
            foreach(string kind in new[]{"StableDiffusionInpaintPipeline","StableDiffusionUpscalePipeline","StableDiffusionXLInpaintPipeline","UnknownPipeline"})
            {string path=Path.Combine(dir,"base","bad-"+kind);CreateBase(path,kind.Contains("XL"),kind);Check(path,false,null);}
            string mismatch=Path.Combine(dir,"base","xl-context-mismatch");CreateBase(mismatch,true);File.WriteAllText(Path.Combine(mismatch,"unet","config.json"),ComponentConfig("unet",768));Check(mismatch,false,null);
            string refiner=Path.Combine(dir,"base","xl-refiner");CreateBase(refiner,true);File.WriteAllText(Path.Combine(refiner,"unet","config.json"),ComponentConfig("unet",1280));Check(refiner,false,null);
            foreach(string component in new[]{"vae/config.json","text_encoder/config.json","scheduler/scheduler_config.json"})
            {string path=Path.Combine(dir,"base","empty-"+component.Replace('/','-'));CreateBase(path,false);File.WriteAllText(Path.Combine(path,component.Replace('/',Path.DirectorySeparatorChar)),"{}");Check(path,false,null);}
            string missingMerges=Path.Combine(dir,"base","missing-merges");CreateBase(missingMerges,false);File.Delete(Path.Combine(missingMerges,"tokenizer","merges.txt"));Check(missingMerges,false,null);
            string largeTokenizer=Path.Combine(dir,"base","valid-large-tokenizer-int");CreateBase(largeTokenizer,false);File.WriteAllText(Path.Combine(largeTokenizer,"tokenizer","tokenizer_config.json"),"{\"tokenizer_class\":\"CLIPTokenizer\",\"model_max_length\":1000000000000000019884624838656}");Check(largeTokenizer,false,"sd15");
            string[] controls={
                "{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":768,\"in_channels\":4}",
                "{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":2048,\"in_channels\":4}",
                "{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":1024,\"in_channels\":4}",
                "{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":768,\"in_channels\":9}",
                "{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":768}",
                "{\"_class_name\":\"ControlNetModel\",\"in_channels\":4}",
                "{\"_class_name\":\"ControlNetModel\",\"cross_attention_dim\":\"768\",\"in_channels\":4}",
                "{\"_class_name\":\"UnknownControl\",\"cross_attention_dim\":768,\"in_channels\":4}", "{}"
            };
            for(int i=0;i<controls.Length;i++){string path=Path.Combine(dir,"controlnet","control-"+i);Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,"config.json"),controls[i]);Tensor(Path.Combine(path,"model.safetensors"));Check(path,true,i==0?"sd15":i==1?"sdxl":null);}
            var local=(List<QrAiModel>)typeof(QrAiService).GetMethod("DiscoverLocal",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{dir});
            Require(local.Count==7&&local.All(m=>m.Name.StartsWith("valid-")||m.Name=="control-0"||m.Name=="control-1"),"bounded local discovery bypassed explicit directory guards");
            var all=QrAiService.Discover(dir).Where(m=>m.Path.StartsWith(dir+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToList();Require(all.Count==7,"external/reuse discovery bypassed directory guards");
            File.WriteAllText(Path.Combine(dir,"PASS.md"),"PASS: "+checks+" directory profile checks. Normal SD1.x/SDXL and Img2Img contracts accepted; SD2, inpainting, refiner, unsupported pipelines, mismatched or absent UNet fields, non-scalar dimensions, missing tokenizer data and malformed/empty configs rejected consistently by explicit and bounded discovery. Small structural fixtures only; no neural inference or downloads.");
        }
    }
}
