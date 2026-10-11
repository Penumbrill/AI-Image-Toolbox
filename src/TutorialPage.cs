using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QQImageSwitch
{
    public sealed class TutorialContent
    {
        public string title,description,contents,back;
        public TutorialChapter[] chapters;
        public static TutorialContent Load()
        {
            using(var stream=typeof(TutorialContent).Assembly.GetManifestResourceStream(L.English?"TutorialEnglish":"TutorialChinese"))
            using(var reader=new StreamReader(stream))return Inspection.Json().Deserialize<TutorialContent>(reader.ReadToEnd());
        }
    }
    public sealed class TutorialChapter
    {
        public string id,title,intro,stepsTitle,optionsTitle,outputTitle,output;
        public string[] steps,options;
    }
    public sealed class TutorialPage : ToolPage
    {
        readonly TableLayoutPanel guide;
        readonly List<Control> anchors=new List<Control>();
        readonly List<Button> links=new List<Button>();
        readonly Action language;
        public TutorialPage(ExportLocation location,Action<bool> changed):base("使用教程","按工具菜单顺序查阅用途、操作步骤、选项与输出。点击目录跳转；教程离线可用。",location,changed)
        {
            Footer.Visible=false;
            guide=Ui.Column(new Padding(0));Ui.Add(Body,guide);Rebuild();
            language=Rebuild;L.Changed+=language;FinishLayout();
        }
        void Rebuild()
        {
            if(IsDisposed)return;
            guide.SuspendLayout();
            try
            {
                while(guide.Controls.Count>0)guide.Controls[0].Dispose();
                guide.RowStyles.Clear();guide.RowCount=0;anchors.Clear();links.Clear();
                var data=TutorialContent.Load();Ui.Add(guide,Ui.Text(data.contents,16,true));
                var toc=Ui.Column(new Padding(0));Ui.Add(guide,toc);
                for(int i=0;i<data.chapters.Length;i++)
                {
                    int index=i;var button=Ui.Button((i+1)+". "+data.chapters[i].title,delegate{Jump(anchors[index]);});
                    button.TextAlign=ContentAlignment.MiddleLeft;button.Dock=DockStyle.Top;
                    Ui.Add(toc,button);links.Add(button);
                }
                for(int i=0;i<data.chapters.Length;i++)
                {
                    var c=data.chapters[i];var card=Ui.Column(new Padding(18));card.BackColor=Color.White;card.Margin=new Padding(0,12,0,4);Ui.Add(guide,card);anchors.Add(card);
                    Ui.Add(card,Ui.Text((i+1)+". "+c.title,17,true));Ui.Add(card,Ui.Text(c.intro));
                    Ui.Add(card,Ui.Text(c.stepsTitle,12,true));for(int j=0;j<c.steps.Length;j++)Ui.Add(card,Ui.Text((j+1)+". "+c.steps[j]));
                    Ui.Add(card,Ui.Text(c.optionsTitle,12,true));foreach(string item in c.options)Ui.Add(card,Ui.Text("• "+item));
                    Ui.Add(card,Ui.Text(c.outputTitle,12,true));Ui.Add(card,Ui.Text(c.output));Ui.Add(card,Ui.Button(data.back,delegate{Scroller.AutoScrollPosition=Point.Empty;}));
                }
            }
            finally{guide.ResumeLayout(true);Scroller.AutoScrollPosition=Point.Empty;}
        }
        void Jump(Control anchor)
        {
            int top=Scroller.PointToClient(anchor.PointToScreen(Point.Empty)).Y-Scroller.AutoScrollPosition.Y;
            Scroller.AutoScrollPosition=new Point(0,Math.Max(0,top-8));
        }
        protected override void Dispose(bool disposing){if(disposing)L.Changed-=language;base.Dispose(disposing);}
#if SELF_TEST
        internal async Task CheckBehavior(string folder)
        {
            Directory.CreateDirectory(folder);var form=(MainForm)FindForm();var prior=form.ClientSize;bool english=L.English;
            try{foreach(bool en in new[]{false,true}){L.Set(en?"en":"zh");foreach(var size in new[]{new Size(900,700),new Size(1500,950)}){form.ClientSize=size;await Task.Delay(80);Application.DoEvents();form.ValidateLayout();if(links.Count!=10||anchors.Count!=10||Footer.Visible)throw new Exception("Tutorial navigation or read-only layout failed");links[9].PerformClick();await Task.Delay(30);if(Scroller.AutoScrollPosition.Y>=-100)throw new Exception("Tutorial chapter jump failed");links[0].PerformClick();await Task.Delay(30);Scroller.AutoScrollPosition=Point.Empty;await Task.Delay(30);using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(folder,"guide-"+(en?"en":"zh")+"-"+size.Width+".png"));}}}File.WriteAllText(Path.Combine(folder,"result.txt"),"PASS: offline bilingual guide, ten chapters, chapter jumps, read-only footer, narrow/wide layouts.");}
            finally{form.ClientSize=prior;L.Set(english?"en":"zh");}
        }
#endif
    }
}
