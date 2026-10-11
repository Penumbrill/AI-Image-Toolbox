using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace QQImageSwitch
{
    public static partial class QrAiService
    {
        static string AiSettingsPath {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AIImageTools","qr-ai.json");}}
        static Dictionary<string,object> ReadObject(string path)
        {try{return Inspection.Json().Deserialize<Dictionary<string,object>>(File.ReadAllText(path))??new Dictionary<string,object>();}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}return new Dictionary<string,object>();}
        public static QrAiOptions LoadOptions(string path=null)
        {
            var options=new QrAiOptions();try{var data=ReadObject(path??AiSettingsPath);string root=Value(data,"Root");object auto;bool savedAuto=data.TryGetValue("AutoRoot",out auto)&&auto is bool?(bool)auto:QrAiStorage.IsLegacyDefault(root);if(!string.IsNullOrWhiteSpace(root)){options.AutoRoot=savedAuto;options.Root=savedAuto&&QrAiStorage.IsLegacyDefault(root)?QrAiStorage.DefaultRoot():Path.GetFullPath(root);}options.Base=Value(data,"Base");options.Control=Value(data,"Control");string family=Value(data,"Family"),environment=Value(data,"Environment");var model=ReadModel(options.Base,false);options.Family=family=="sd15"||family=="sdxl"?family:model==null?"sd15":model.Family;options.Environment=environment=="cpu"?"cpu":"cuda";
                var saved=Inspection.Json().Deserialize<QrAiOptions>(Inspection.Json().Serialize(data));options.Selections=(saved.Selections??new Dictionary<string,QrAiSelection>()).Where(p=>p.Value!=null&&(p.Key=="sd15"||p.Key=="sdxl")).ToDictionary(p=>p.Key,p=>p.Value);options.Profiles=new Dictionary<string,QrAiGenerationProfile>();foreach(var pair in (saved.Profiles??new Dictionary<string,QrAiGenerationProfile>()).Take(128))try{if(pair.Value!=null){pair.Value.Validate(pair.Value.Family);options.Profiles[pair.Key]=pair.Value;}}catch(ArgumentException){}
            }catch(ArgumentException){}catch(InvalidOperationException){}return options;
        }
        public static void SaveOptions(QrAiOptions options,string path=null)
        {
            string target=path??AiSettingsPath;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));var history=new List<string>();
            var data=ReadObject(target);object roots;if(data.TryGetValue("Roots",out roots)&&roots is System.Collections.IEnumerable)foreach(object root in (System.Collections.IEnumerable)roots)if(root is string)history.Add((string)root);
            string oldRoot=Value(data,"Root");if(!string.IsNullOrWhiteSpace(oldRoot))history.Add(oldRoot);history.Insert(0,Path.GetFullPath(options.Root));history=history.Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList();
            RememberSelection(options);Codec.SaveAtomic(target,System.Text.Encoding.UTF8.GetBytes(Inspection.Json().Serialize(new{Root=Path.GetFullPath(options.Root),AutoRoot=options.AutoRoot,Base=options.Base,Control=options.Control,Family=options.Family,Environment=options.Environment,Selections=options.Selections,Profiles=options.Profiles,Roots=history})));
            if(path==null)QrAiStorage.RegisterSelected(options);
        }
        public static string[] ModelSearchRoots(string root)
        {
            var result=new List<string>();Action<string> add=delegate(string p){try{if(!string.IsNullOrWhiteSpace(p)&&Directory.Exists(p)){p=Path.GetFullPath(p);if(!result.Contains(p,StringComparer.OrdinalIgnoreCase))result.Add(p);}}catch(ArgumentException){}catch(IOException){}catch(UnauthorizedAccessException){}};
            add(root);add(new QrAiOptions().Root);add(QrAiStorage.LegacyDefaultRoot);
            var saved=ReadObject(AiSettingsPath);object savedRoots;if(saved.TryGetValue("Roots",out savedRoots)&&savedRoots is System.Collections.IEnumerable)foreach(object p in (System.Collections.IEnumerable)savedRoots)if(p is string)add((string)p);foreach(string key in new[]{"Base","Control"}){string p=Value(saved,key);if(File.Exists(p))add(Path.GetDirectoryName(p));else add(p);}
            foreach(string name in new[]{"HF_HOME","HF_HUB_CACHE","HUGGINGFACE_HUB_CACHE","COMFYUI_PATH","SD_WEBUI_PATH"})add(Environment.GetEnvironmentVariable(name));
            string user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);add(Path.Combine(user,".cache","huggingface"));
            foreach(string start in new[]{user,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Directory.GetCurrentDirectory(),AppDomain.CurrentDomain.BaseDirectory})
            {
                string parent=start;for(int level=0;level<4&&!string.IsNullOrEmpty(parent);level++)
                {
                    foreach(string name in new[]{"models","ComfyUI","stable-diffusion-webui","sd-webui","webui"})add(Path.Combine(parent,name));
                    // Only one level, narrowly named installations, bounded count.
                    try{foreach(string pattern in new[]{"*ComfyUI*","*sd-webui*","*stable-diffusion-webui*"})foreach(string dir in Directory.GetDirectories(parent,pattern).Take(12))add(dir);}catch(IOException){}catch(UnauthorizedAccessException){}
                    parent=Path.GetDirectoryName(parent.TrimEnd(Path.DirectorySeparatorChar));
                }
            }
            foreach(string install in result.ToArray())
            {
                foreach(string suffix in new[]{"models","webui/models","models/diffusers","models/Stable-diffusion","models/checkpoints","models/ControlNet","models/controlnet","extensions/sd-webui-controlnet/models"})add(Path.Combine(install,suffix.Replace('/',Path.DirectorySeparatorChar)));
                // Read only known configuration path fields, never execute a launcher.
                foreach(string file in new[]{Path.Combine(install,"config.json"),Path.Combine(install,"webui","config.json")})try
                {
                    if(!File.Exists(file)||new FileInfo(file).Length>1024*1024)continue;var settings=ReadObject(file);
                    foreach(string key in new[]{"ckpt_dir","control_net_models_path","controlnet_model_paths","diffusers_dir"}){string p=Value(settings,key);if(!string.IsNullOrEmpty(p))add(Path.IsPathRooted(p)?p:Path.Combine(Path.GetDirectoryName(file),p));}
                }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}
            }
            return result.Take(64).ToArray();
        }
        public static string ResolveModelPackage(string root,string target)
        {
            try{var data=ReadObject(Path.Combine(root,"model-reuse.json"));var packages=data["packages"] as Dictionary<string,object>;object value;if(packages!=null&&packages.TryGetValue(target,out value)){var entry=value as Dictionary<string,object>;if(entry!=null&&Directory.Exists(Value(entry,"path")))return Value(entry,"path");}}catch(IOException){}catch(ArgumentException){}catch(KeyNotFoundException){}
            return Path.Combine(root,target.Replace('/',Path.DirectorySeparatorChar));
        }
        public static string ModelFolder(string path)
        {
            try{if(string.IsNullOrWhiteSpace(path))return null;if(File.Exists(path))return Path.GetDirectoryName(Path.GetFullPath(path));if(Directory.Exists(path))return Path.GetFullPath(path);}catch(ArgumentException){}catch(IOException){}catch(UnauthorizedAccessException){}return null;
        }
        static bool DirectoryModelComplete(string dir,bool control)
        {
            try
            {
                string family=DirectoryFamily(dir,control);if(family==null)return false;
                if(control){var files=Directory.GetFiles(dir,"*.safetensors");return files.Length>0&&files.All(f=>{var i=QrAiIntegrity.Inspect(f);return i!=null&&i.TensorCount>0;});}
                bool xl=family=="sdxl";
                foreach(string component in xl?new[]{"unet","vae","text_encoder","text_encoder_2"}:new[]{"unet","vae","text_encoder"}){string folder=Path.Combine(dir,component);if(ReadObject(Path.Combine(folder,"config.json")).Count==0||!Directory.Exists(folder))return false;var files=Directory.GetFiles(folder,"*.safetensors");if(files.Length==0||files.Any(f=>{var i=QrAiIntegrity.Inspect(f);return i==null||i.TensorCount==0;}))return false;}
                // Tokenizer metadata can contain a model_max_length integer beyond
                // .NET's JSON numeric range; Python handles that valid metadata.
                if(ReadObject(Path.Combine(dir,"scheduler","scheduler_config.json")).Count==0||!File.Exists(Path.Combine(dir,"tokenizer","tokenizer_config.json"))||!File.Exists(Path.Combine(dir,"tokenizer","vocab.json"))||!File.Exists(Path.Combine(dir,"tokenizer","merges.txt")))return false;
                if(xl&&(!File.Exists(Path.Combine(dir,"tokenizer_2","tokenizer_config.json"))||!File.Exists(Path.Combine(dir,"tokenizer_2","vocab.json"))||!File.Exists(Path.Combine(dir,"tokenizer_2","merges.txt"))))return false;
                return true;
            }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        }
        internal static QrAiModel ReadModel(string path,bool? role=null)
        {
            try
            {
                if(string.IsNullOrWhiteSpace(path))return null;
                if(File.Exists(path))
                {
                    if(!path.EndsWith(".safetensors",StringComparison.OrdinalIgnoreCase))return null;var info=QrAiIntegrity.Inspect(path);if(info==null||info.Family==null||(info.Role!="base"&&info.Role!="control"))return null;
                    bool control=info.Role=="control";if(!control&&new FileInfo(path).Length<100000000)return null;if(role.HasValue&&role.Value!=control)return null;return new QrAiModel{Path=path,Name=Path.GetFileName(path),Family=info.Family,Control=control,Integrity="结构校验通过"};
                }
                string config=Path.Combine(path,"model_index.json");bool isControl=false;if(!File.Exists(config)){config=Path.Combine(path,"config.json");isControl=true;}if(!File.Exists(config)||role.HasValue&&role.Value!=isControl)return null;
                string family=DirectoryFamily(path,isControl);if(family==null||!DirectoryModelComplete(path,isControl))return null;
                return new QrAiModel{Path=path,Name=Path.GetFileName(path),Control=isControl,Family=family,Integrity="结构校验通过"};
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}return null;
        }
        public static List<QrAiModel> Discover(string root)
        {
            var result=DiscoverLocal(root);var starts=new List<string>(ModelSearchRoots(root));
            try{var data=ReadObject(Path.Combine(root,"model-reuse.json"));var packages=data["packages"] as Dictionary<string,object>;if(packages!=null)foreach(var pair in packages)if(pair.Key.StartsWith("base/")||pair.Key.StartsWith("controlnet/")){var record=pair.Value as Dictionary<string,object>;if(record!=null){var model=ReadModel(Value(record,"path"));if(model!=null){model.Integrity="官方 SHA 生成前复核";result.Add(model);}}}}catch(IOException){}catch(ArgumentException){}catch(KeyNotFoundException){}
            var manifest=PinnedPackages();foreach(string start in starts.ToArray())foreach(var package in manifest){string target=Value(package,"target");if(!target.StartsWith("base/")&&!target.StartsWith("controlnet/"))continue;starts.Add(Path.Combine(start,target.Replace('/',Path.DirectorySeparatorChar)));string repo="models--"+Value(package,"repo").Replace("/","--");starts.Add(Path.Combine(start,repo,"snapshots",Value(package,"revision")));starts.Add(Path.Combine(start,"hub",repo,"snapshots",Value(package,"revision")));}
            foreach(string start in starts.Distinct(StringComparer.OrdinalIgnoreCase).Take(256))
            {
                var model=ReadModel(start);if(model!=null)result.Add(model);
                if(!Directory.Exists(start))continue;
                try{foreach(string file in Directory.GetFiles(start,"*.safetensors").Take(32)){model=ReadModel(file);if(model!=null)result.Add(model);}foreach(string child in Directory.GetDirectories(start).Take(32)){model=ReadModel(child);if(model!=null)result.Add(model);}}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            return result.GroupBy(m=>m.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.OrderByDescending(m=>m.Integrity=="官方 SHA 生成前复核").First()).ToList();
        }
        static List<Dictionary<string,object>> PinnedPackages()
        {
            using(var stream=typeof(QrAiService).Assembly.GetManifestResourceStream("AiQrModels"))using(var reader=new StreamReader(stream)){var manifest=Inspection.Json().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());return ((System.Collections.IEnumerable)manifest["packages"]).Cast<object>().OfType<Dictionary<string,object>>().ToList();}
        }
        static string PinnedTarget(string path,string root)
        {
            if(!Directory.Exists(path))return null;
            var manifest=PinnedPackages();
            foreach(var package in manifest)
            {
                string target=Value(package,"target");if(!target.StartsWith("base/")&&!target.StartsWith("controlnet/"))continue;
                if(string.Equals(Path.GetFullPath(Path.Combine(root,target.Replace('/',Path.DirectorySeparatorChar))),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))return target;
                var parent=Directory.GetParent(path);if(parent!=null&&parent.Name=="snapshots"&&parent.Parent!=null&&parent.Parent.Name=="models--"+Value(package,"repo").Replace("/","--")&&Path.GetFileName(path)==Value(package,"revision"))return target;
            }
            try{var data=ReadObject(Path.Combine(root,"model-reuse.json"));var packages=data["packages"] as Dictionary<string,object>;if(packages!=null)foreach(var pair in packages)if(pair.Key.StartsWith("base/")||pair.Key.StartsWith("controlnet/")){var record=pair.Value as Dictionary<string,object>;var pinned=manifest.FirstOrDefault(p=>Value(p,"target")==pair.Key);if(record!=null&&pinned!=null&&PinnedFilesMatch(record,pinned)&&string.Equals(Path.GetFullPath(Value(record,"path")),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase))return pair.Key;}}catch(IOException){}catch(ArgumentException){}catch(KeyNotFoundException){}
            return null;
        }
        static bool PinnedFilesMatch(Dictionary<string,object> record,Dictionary<string,object> package)
        {
            object actual,expected;if(!record.TryGetValue("files",out actual)||!package.TryGetValue("files",out expected)||!(actual is System.Collections.IEnumerable)||actual is string)return false;
            var a=((System.Collections.IEnumerable)actual).Cast<object>().ToArray();var b=((System.Collections.IEnumerable)expected).Cast<object>().ToArray();if(a.Length==0||a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++){var x=a[i] as Dictionary<string,object>;var y=b[i] as Dictionary<string,object>;if(x==null||y==null)return false;foreach(string key in new[]{"local","path","size","sha256"})if(Value(x,key)!=Value(y,key))return false;}return true;
        }
        static Dictionary<string,string> PinnedSelections(QrAiOptions options)
        {return new Dictionary<string,string>{{"base",PinnedTarget(options.Base,options.Root)},{"controlnet",PinnedTarget(options.Control,options.Root)}};}
        static string[] CustomWeightKinds(QrAiOptions options)
        {var kinds=new List<string>();if(ReadModel(options.Base,false)!=null&&PinnedTarget(options.Base,options.Root)==null)kinds.Add("base");if(ReadModel(options.Control,true)!=null&&PinnedTarget(options.Control,options.Root)==null)kinds.Add("controlnet");return kinds.ToArray();}
        static string[] DownloadSearchRoots(QrAiOptions options)
        {var roots=new List<string>{options.Root};foreach(string p in new[]{options.Base,options.Control})if(!string.IsNullOrWhiteSpace(p)){string dir=File.Exists(p)?Path.GetDirectoryName(p):p;for(int i=0;i<3&&!string.IsNullOrEmpty(dir);i++){if(Directory.Exists(dir))roots.Add(dir);dir=Path.GetDirectoryName(dir);}}roots.AddRange(ModelSearchRoots(options.Root));return roots.Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray();}
        static void ResolveSelectedPinned(QrAiOptions options,Dictionary<string,string> pinned)
        {foreach(var pair in pinned){bool control=pair.Key=="controlnet";string target=pair.Value;if(string.IsNullOrEmpty(target)){if(!string.IsNullOrEmpty(control?options.Control:options.Base))continue;target=control?"controlnet/qr-monster-"+options.Family:options.Family=="sdxl"?"base/sdxl-base":"base/dreamshaper-8";}string resolved=ResolveModelPackage(options.Root,target);if(DirectoryModelComplete(resolved,control)){if(control)options.Control=resolved;else options.Base=resolved;}}}
        public static async Task CheckModels(QrAiOptions options,Action<string> progress,CancellationToken token)
        {
            string worker=Materialize(options.Root);QrAiStorage.RegisterEnvironment(options.Root,options.Environment);await PortablePython(options.Root,progress,token,options.Environment);await VerifySelectedModels(options,progress,token);
            var baseModel=ReadModel(options.Base,false);var controlModel=ReadModel(options.Control,true);string family=baseModel!=null?baseModel.Family:controlModel!=null?controlModel.Family:options.Family;
            var result=await Run(Python(options.Root,options.Environment),worker,new Dictionary<string,object>{{"operation","download"},{"environment",options.Environment},{"models",options.Root},{"manifest",Path.Combine(options.Root,"engine","models.json")},{"family",family},{"search_roots",DownloadSearchRoots(options)},{"skip_weights",CustomWeightKinds(options)},{"inspection_only",true},{"force_hash",true}},progress,null,token);
            progress(L.T("模型扫描与完整性校验完成，缺失文件：")+Value(result,"missing"));
        }
        static async Task VerifySelectedModels(QrAiOptions options,Action<string> progress,CancellationToken token)
        {
            await Task.Run(delegate
            {
                var hashes=ReadObject(Path.Combine(options.Root,"custom-model-hashes.json"));
                foreach(string selected in new[]{options.Base,options.Control})
                {
                    if(string.IsNullOrEmpty(selected))continue;token.ThrowIfCancellationRequested();
                    var model=ReadModel(selected);if(model==null)throw new InvalidDataException("模型文件结构不完整或已截断，请重新选择模型。");
                    if(PinnedTarget(selected,options.Root)!=null)continue;
                    var files=new List<string>();if(File.Exists(selected))files.Add(selected);else foreach(string component in model.Control?new[]{""}:model.Family=="sdxl"?new[]{"unet","vae","text_encoder","text_encoder_2"}:new[]{"unet","vae","text_encoder"})
                    {
                        token.ThrowIfCancellationRequested();var found=Directory.EnumerateFiles(Path.Combine(selected,component),"*.safetensors",SearchOption.TopDirectoryOnly).Take(257).ToArray();
                        if(found.Length>256)throw new InvalidDataException("模型组件文件过多，请选择完整模型目录。");files.AddRange(found);
                    }
                    foreach(string file in files)
                    {
                        var info=QrAiIntegrity.Inspect(file,true,delegate(long bytes,long total){progress(L.T("校验模型文件：")+"\n"+Path.GetFileName(file)+" · "+SizeText(bytes)+" / "+SizeText(total));},token);
                        if(info==null)throw new InvalidDataException("模型文件结构不完整或已截断，请重新选择模型。");object prior;if(hashes.TryGetValue(file,out prior)){var old=prior as Dictionary<string,object>;if(old!=null&&Value(old,"Sha256")!=info.Sha256)progress(L.T("自定义模型内容已变化，本次已重新校验。")+" "+Path.GetFileName(file));}hashes[file]=new{Sha256=info.Sha256,Size=info.FileLength,LastWriteTimeUtc=File.GetLastWriteTimeUtc(file).Ticks};
                    }
                }
                Directory.CreateDirectory(options.Root);Codec.SaveAtomic(Path.Combine(options.Root,"custom-model-hashes.json"),System.Text.Encoding.UTF8.GetBytes(Inspection.Json().Serialize(hashes)));
            },token).ConfigureAwait(false);
        }
    }
}
