using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace QQImageSwitch
{
    static class L
    {
        public static bool English {get;private set;}
        public static event Action Changed;
        static readonly Dictionary<string,string> Catalog=Load();
        sealed class Template
        {
            internal readonly Regex Pattern;internal readonly string Value,Prefix;
            internal Template(string format,string value){Value=value;var slot=Regex.Match(format,@"\{\d+\}");Prefix=format.Substring(0,slot.Index);Pattern=new Regex("^"+Regex.Replace(Regex.Escape(format),@"\\\{\d+}","(.*?)")+"$",RegexOptions.Singleline);}
            internal Match Match(string text){return text.StartsWith(Prefix,StringComparison.Ordinal)?Pattern.Match(text):null;}
        }
        static readonly List<Template> Templates=Catalog.Where(p=>Regex.IsMatch(p.Key,@"\{\d+\}")).OrderByDescending(p=>Regex.Replace(p.Key,@"\{\d+\}","").Length).Select(p=>new Template(p.Key,p.Value)).ToList();
        static readonly List<Template> ReverseTemplates=Catalog.Where(p=>Regex.IsMatch(p.Value,@"\{\d+\}")).OrderByDescending(p=>Regex.Replace(p.Value,@"\{\d+\}","").Length).Select(p=>new Template(p.Value,p.Key)).ToList();
        static readonly Dictionary<string,string> ReverseCatalog=Catalog.GroupBy(p=>p.Value).ToDictionary(g=>g.Key,g=>g.First().Key);
        static readonly Dictionary<string,string> TranslationCache=new Dictionary<string,string>(),CanonicalCache=new Dictionary<string,string>();
        static readonly object CacheLock=new object();
        static bool Cached(Dictionary<string,string> cache,string source,out string value){lock(CacheLock)return cache.TryGetValue(source,out value);}
        static string Remember(Dictionary<string,string> cache,string source,string result){if(source.Length<=4096)lock(CacheLock){if(cache.Count>=2048)cache.Clear();cache[source]=result;}return result;}
        static readonly ConditionalWeakTable<Control,Binding> Bound=new ConditionalWeakTable<Control,Binding>();
        static Dictionary<string,string> Load(){using(var stream=typeof(L).Assembly.GetManifestResourceStream("EnglishStrings"))using(var reader=new StreamReader(stream))return Inspection.Json().Deserialize<Dictionary<string,string>>(reader.ReadToEnd());}
        public static string T(string source)
        {
            if(!English||string.IsNullOrEmpty(source))return source;string result;if(Catalog.TryGetValue(source,out result))return result;
            if(Cached(TranslationCache,source,out result))return result;
            foreach(var pair in Templates){var m=pair.Match(source);if(m!=null&&m.Success){bool keepFolder=source.StartsWith("自动记住根目录；");string[] args=m.Groups.Cast<Group>().Skip(1).Select(g=>keepFolder?g.Value:T(g.Value)).ToArray();return Remember(TranslationCache,source,Regex.Replace(pair.Value,@"\{(\d+)\}",x=>args[int.Parse(x.Groups[1].Value)]));}}
            return Remember(TranslationCache,source,source.Contains("\n")?string.Join("\n",source.Split('\n').Select(T)):source);
        }
        public static void Set(string language){bool next=language=="en";if(next==English)return;English=next;if(Changed!=null)Changed();}
        public static void Bind(Control control)
        {
            Binding existing;if(control==null||control.IsDisposed||Bound.TryGetValue(control,out existing))return;var binding=new Binding(control);Bound.Add(control,binding);binding.Attach();
        }
        sealed class Binding
        {
            readonly Control c;string source;bool applying;readonly bool caption;
            public Binding(Control control){c=control;caption=(c.Tag as string)!="raw"&&(c is Label||c is Button||c is CheckBox||c is Form||(c is TextBox&&((TextBox)c).ReadOnly));source=caption?Canonical(c.Text):string.Empty;}
            public void Attach(){if(caption)c.TextChanged+=TextChanged;c.ControlAdded+=Added;c.Disposed+=Disposed;Changed+=Refresh;if(!(c is UpDownBase)&&!(c is TextBoxBase)&&!(c is ComboBox))foreach(Control child in c.Controls)Bind(child);Refresh();}
            void Added(object sender,ControlEventArgs e){if(!(c is UpDownBase)&&!(c is TextBoxBase)&&!(c is ComboBox))Bind(e.Control);}
            void TextChanged(object sender,EventArgs e){if(applying)return;source=Canonical(c.Text);Refresh();}
            void Refresh(){if(c.IsDisposed||c.Disposing)return;applying=true;try{if(caption){string next=T(source);if(c.Text!=next)c.Text=next;}c.Invalidate();}finally{applying=false;}}
            void Disposed(object sender,EventArgs e){Changed-=Refresh;c.ControlAdded-=Added;if(caption)c.TextChanged-=TextChanged;}
        }
        public static string Canonical(string source)
        {
            if(!English||string.IsNullOrEmpty(source))return source;
            if(Catalog.ContainsKey(source))return source;string result;if(ReverseCatalog.TryGetValue(source,out result)||Cached(CanonicalCache,source,out result))return result;
            foreach(var p in ReverseTemplates){var m=p.Match(source);if(m!=null&&m.Success){var args=m.Groups.Cast<Group>().Skip(1).Select(g=>g.Value).ToArray();return Remember(CanonicalCache,source,Regex.Replace(p.Value,@"\{(\d+)\}",x=>args[int.Parse(x.Groups[1].Value)]));}}
            return Remember(CanonicalCache,source,source.Contains("\n")?string.Join("\n",source.Split('\n').Select(Canonical)):source);
        }
        public static DialogResult Show(Form dialog,IWin32Window owner){return dialog.ShowDialog(owner);}
        public static DialogResult Show(CommonDialog dialog,IWin32Window owner)
        {
            var fd=dialog as FileDialog;if(fd!=null){fd.Title=T(fd.Title);fd.Filter=T(fd.Filter);}var folder=dialog as FolderBrowserDialog;if(folder!=null)folder.Description=T(folder.Description);return dialog.ShowDialog(owner);
        }
        public static string[] Untranslated(IEnumerable<string> source){return source.Where(s=>Regex.IsMatch(s,@"[\u4e00-\u9fff]")&&T(s)==s).Distinct().ToArray();}
    }
}
