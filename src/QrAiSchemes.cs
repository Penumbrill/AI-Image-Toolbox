using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QQImageSwitch
{
    public sealed class QrAiSelection
    {
        public string Base,Control,Environment="cuda";
    }
    public sealed class QrAiGenerationProfile
    {
        public string Family,Id,PromptTemplate="",NegativeTemplate="";
        public int Steps,ClipSkip;public double Guidance,Strength;
        public Dictionary<string,object> Request(){return new Dictionary<string,object>{{"id",Id},{"prompt_template",PromptTemplate},{"negative_template",NegativeTemplate},{"steps",Steps},{"guidance",Guidance},{"strength",Strength},{"clip_skip",ClipSkip}};}
        public void Validate(string family)
        {
            if(Family!=family||!new[]{"sd15","sdxl"}.Contains(family))throw new ArgumentException("模型提示词方案与模型类型不匹配。");
            if(Id!="custom"&&Id!=family+"-standard"&&!(family=="sd15"&&Id=="dreamshaper-8")&&!(family=="sdxl"&&Id=="sdxl-base-1.0"))throw new ArgumentException("请选择支持的模型提示词方案。");
            if(Steps!=0&&(Steps<8||Steps>60)||double.IsNaN(Guidance)||double.IsInfinity(Guidance)||Guidance!=0&&(Guidance<1||Guidance>12)||double.IsNaN(Strength)||double.IsInfinity(Strength)||Strength!=0&&(Strength<.15||Strength>.85)||ClipSkip<0||ClipSkip>2||family=="sdxl"&&ClipSkip!=0)throw new ArgumentException("模型生成参数超出支持范围。");
            if(Id=="custom"&&(PromptTemplate==null||PromptTemplate.Length>1800||!PromptTemplate.Contains("{prompt}")||NegativeTemplate==null||NegativeTemplate.Length>1800||!NegativeTemplate.Contains("{negative}")))throw new ArgumentException("自定义模板必须保留 {prompt} 与 {negative} 占位符。");
            if(Id=="custom"&&(PromptTemplate.Replace("{prompt}","").IndexOfAny(new[]{'{','}'})>=0||NegativeTemplate.Replace("{negative}","").IndexOfAny(new[]{'{','}'})>=0))throw new ArgumentException("自定义模板必须保留 {prompt} 与 {negative} 占位符。");
        }
    }
    public static partial class QrAiService
    {
        public static QrAiGenerationProfile ProfileFor(QrAiOptions options)
        {
            QrAiGenerationProfile profile;string path=options.Base;
            if(path!=null&&options.Profiles!=null&&options.Profiles.TryGetValue(path,out profile)&&profile!=null){profile.Validate(options.Family);return profile;}
            string name=path==null?"":Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
            string id=options.Family=="sdxl"?(path==null||name.Equals("sdxl-base",StringComparison.OrdinalIgnoreCase)?"sdxl-base-1.0":"sdxl-standard"):(path==null||name.Equals("dreamshaper-8",StringComparison.OrdinalIgnoreCase)?"dreamshaper-8":"sd15-standard");
            return new QrAiGenerationProfile{Family=options.Family,Id=id};
        }
        public static void ValidateScheme(QrAiOptions options)
        {
            if(options==null||options.Family!="sd15"&&options.Family!="sdxl")throw new ArgumentException("请选择支持的模型类型。");RuntimeFolder(options.Root,options.Environment);
            foreach(var pair in new[]{Tuple.Create(options.Base,false),Tuple.Create(options.Control,true)})if(!string.IsNullOrWhiteSpace(pair.Item1))
            {
                var model=ReadModel(pair.Item1,pair.Item2);if(model==null)throw new InvalidOperationException("所选模型不完整或类型不受支持，请重新扫描 / 校验模型。");if(model.Family!=options.Family)throw new InvalidOperationException("基础模型与二维码 ControlNet 的类型不匹配。");
            }
            ProfileFor(options).Validate(options.Family);
        }
        public static void RememberSelection(QrAiOptions options)
        {
            if(options.Selections==null)options.Selections=new Dictionary<string,QrAiSelection>();options.Selections[options.Family]=new QrAiSelection{Base=options.Base,Control=options.Control,Environment=options.Environment};
        }
        public static QrAiOptions CopyOptions(QrAiOptions options){return Inspection.Json().Deserialize<QrAiOptions>(Inspection.Json().Serialize(options));}
        public static void UseRecommendedScheme(QrAiOptions options)
        {
            RememberSelection(options);options.Family="sd15";options.Environment="cuda";options.Base=options.Control=null;
            if(options.Profiles!=null)options.Profiles.Remove(ResolveModelPackage(options.Root,"base/dreamshaper-8"));RememberSelection(options);
        }
        public static bool RecommendedScheme(QrAiOptions options)
        {
            if(options.Family!="sd15"||options.Environment!="cuda")return false;
            foreach(var pair in new[]{Tuple.Create(options.Base,"base/dreamshaper-8"),Tuple.Create(options.Control,"controlnet/qr-monster-sd15")})if(pair.Item1!=null&&!string.Equals(Path.GetFullPath(pair.Item1),Path.GetFullPath(ResolveModelPackage(options.Root,pair.Item2)),StringComparison.OrdinalIgnoreCase))return false;
            return ProfileFor(options).Id!="custom";
        }
        public static bool KnownQrControl(QrAiModel model)
        {
            if(model==null||!model.Control)return false;string name=Path.GetFileName(model.Path.TrimEnd(Path.DirectorySeparatorChar)).ToLowerInvariant();return name.Contains("qr-monster")||name.Contains("qrcode")||name.Contains("qr_code")||name.Contains("qr-pattern");
        }
    }
}
