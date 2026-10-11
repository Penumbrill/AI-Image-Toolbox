using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QQImageSwitch
{
    public class CoverTextOptions
    {
        public string Text="";
        // Keep legacy IDs 0..3 so existing remembered positions remain valid.
        public int Position;
        public int X=50,Y=50;
        public int SizePercent=7;
        public Color Color=Color.White;
        public readonly List<CoverTextOptions> Extra=new List<CoverTextOptions>();
        public void Assign(CoverTextOptions other){Text=other.Text;Position=other.Position;X=other.X;Y=other.Y;SizePercent=other.SizePercent;Color=other.Color;}
        public CoverTextOptions Copy(){var result=new CoverTextOptions();result.Assign(this);foreach(var item in Extra.Take(31)){var layer=new CoverTextOptions();layer.Assign(item);result.Extra.Add(layer);}return result;}
        public class LayerData {public string Text;public int Position,X,Y,Size,Ink;}
        public string SaveLayers(){return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(Inspection.Json().Serialize(Extra.Select(t=>new LayerData{Text=t.Text,Position=t.Position,X=t.X,Y=t.Y,Size=t.SizePercent,Ink=t.Color.ToArgb()}).ToArray())));}
        public void LoadLayers(string value){if(string.IsNullOrEmpty(value)||value.Length>65536)return;try{var data=Inspection.Json().Deserialize<LayerData[]>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)));if(data==null)return;Extra.Clear();foreach(var d in data.Where(item=>item!=null).Take(31))Extra.Add(new CoverTextOptions{Text=(d.Text??"").Substring(0,Math.Min(200,(d.Text??"").Length)),Position=Math.Max(0,Math.Min(9,d.Position)),X=Math.Max(0,Math.Min(100,d.X)),Y=Math.Max(0,Math.Min(100,d.Y)),SizePercent=Math.Max(2,Math.Min(18,d.Size)),Color=Color.FromArgb(255,Color.FromArgb(d.Ink))});}catch(ArgumentException){}catch(InvalidOperationException){} }
        public void Draw(Bitmap bitmap,bool numbered)
        {DrawOne(bitmap,numbered);foreach(var layer in Extra.Take(31))layer.DrawOne(bitmap,numbered);}
        void DrawOne(Bitmap bitmap,bool numbered)
        {
            string text=(Text??"").Trim();if(text.Length==0)return;
            if(text.Length>200)throw new ArgumentException("封面文字最多 200 个字。");
            float unit=Math.Min(bitmap.Width,bitmap.Height),margin=Math.Max(1,unit*.04f);
            float available=Math.Max(1,bitmap.Width-margin*2);
            if((Position==1||Position==2||Position==9)&&numbered)available=Math.Max(1,available-Math.Max(24,unit*.12f)-margin);
            int column=Position==1||Position==6||Position==8?2:Position==4||Position==7||Position==9?0:1;
            int row=Position==4||Position==5||Position==6?0:Position==1||Position==2||Position==9?2:1;
            using(var g=Graphics.FromImage(bitmap))
            using(var format=new StringFormat {Alignment=column==2?StringAlignment.Far:column==0?StringAlignment.Near:StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})
            {
                float size=Math.Max(1,unit*Math.Max(2,Math.Min(18,SizePercent))/100f);
                using(var probe=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel))
                {float measured=g.MeasureString(text,probe).Width;if(measured>available)size=Math.Max(1,size*available/measured);}
                float height=Math.Min(bitmap.Height-margin*2,size*1.8f);
                var bounds=new RectangleF(margin,row==0?margin:row==1?(bitmap.Height-height)/2:bitmap.Height-margin-height,available,Math.Max(1,height));
                if(Position==3)
                {
                    using(var probe=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel))
                    {
                        float width=Math.Min(available,g.MeasureString(text,probe).Width+size*.12f);
                        float x=Math.Max(margin,Math.Min(bitmap.Width-margin-width,bitmap.Width*Math.Max(0,Math.Min(100,X))/100f-width/2));
                        float y=Math.Max(margin,Math.Min(bitmap.Height-margin-height,bitmap.Height*Math.Max(0,Math.Min(100,Y))/100f-height/2));
                        bounds=new RectangleF(x,y,width,Math.Max(1,height));
                    }
                }
                using(var path=new GraphicsPath())
                using(var ink=new SolidBrush(Color))
                using(var outline=new Pen(Color.GetBrightness()>.5f?System.Drawing.Color.FromArgb(180,30,25,45):System.Drawing.Color.FromArgb(200,255,255,255),Math.Max(1,size*.065f)){LineJoin=LineJoin.Round})
                using(var family=new FontFamily("Microsoft YaHei UI"))
                {
                    path.AddString(text,family,(int)FontStyle.Bold,size,bounds,format);
                    g.SmoothingMode=SmoothingMode.AntiAlias;g.DrawPath(outline,path);g.FillPath(ink,path);
                }
            }
        }
    }
    public class TextPositionEventArgs : EventArgs {public int X,Y;public bool Finished;}
    public class CoverCanvas : ImageCanvas
    {
        public bool TextPlacement;
        public bool PlacementDragging {get{return dragging;}}
        RectangleF placementRegion;
        string placementLabel="";
        // Placement hints belong to the preview control, never to the exported bitmap.
        public RectangleF PlacementRegion {get{return placementRegion;}set{if(placementRegion==value)return;placementRegion=value;Invalidate();}}
        public string PlacementLabel {get{return placementLabel;}set{value=value??"";if(placementLabel==value)return;placementLabel=value;Invalidate();}}
        bool dragging,pending;Point pendingPoint;int lastX=-1,lastY=-1;
        readonly Timer dragTimer=new Timer{Interval=33};
        public event EventHandler<TextPositionEventArgs> PositionChanged;
        public CoverCanvas(){dragTimer.Tick+=delegate{if(pending){pending=false;Position(pendingPoint,false);}};}
        void Position(Point p,bool finished)
        {
            if(Image==null)return;
            var point=ToImage(p);
            int x=(int)Math.Round(point.X*100.0/Image.Width),y=(int)Math.Round(point.Y*100.0/Image.Height);
            x=Math.Max(0,Math.Min(100,x));y=Math.Max(0,Math.Min(100,y));if(!finished&&x==lastX&&y==lastY)return;lastX=x;lastY=y;if(!finished)Interact();
            if(PositionChanged!=null)PositionChanged(this,new TextPositionEventArgs {X=x,Y=y,Finished=finished});
        }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(TextPlacement&&Image!=null&&e.Button==MouseButtons.Left){dragging=true;pending=false;lastX=lastY=-1;Capture=true;Position(e.Location,false);dragTimer.Start();}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging){pendingPoint=e.Location;pending=true;}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(dragging){dragging=false;pending=false;dragTimer.Stop();Position(e.Location,true);Capture=false;}}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture&&dragging){dragging=false;dragTimer.Stop();if(pending){pending=false;Position(pendingPoint,true);}}}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);if(Image==null||placementRegion.Width<=0||placementRegion.Height<=0)return;
            var location=ToScreen(placementRegion.Location);
            var bounds=new RectangleF(location.X,location.Y,(float)(placementRegion.Width*PixelScale),(float)(placementRegion.Height*PixelScale));
            if(float.IsNaN(bounds.X)||float.IsNaN(bounds.Y)||float.IsNaN(bounds.Width)||float.IsNaN(bounds.Height)||float.IsInfinity(bounds.X)||float.IsInfinity(bounds.Y)||float.IsInfinity(bounds.Width)||float.IsInfinity(bounds.Height))return;
            var clip=RectangleF.Intersect(ImageBounds,ClientRectangle);var visible=RectangleF.Intersect(bounds,clip);
            if(visible.Width<=0||visible.Height<=0)return;
            var g=e.Graphics;var state=g.Save();
            try
            {
                g.SetClip(clip,CombineMode.Intersect);g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var shadow=new Pen(Color.FromArgb(230,255,255,255),4))
                using(var outline=new Pen(Color.FromArgb(130,91,191),2)){outline.DashStyle=DashStyle.Dash;g.DrawRectangle(shadow,bounds.X,bounds.Y,bounds.Width,bounds.Height);g.DrawRectangle(outline,bounds.X,bounds.Y,bounds.Width,bounds.Height);}
                if(placementLabel.Length==0||visible.Width<30||visible.Height<20)return;
                var labelBounds=new RectangleF(visible.X+6,visible.Y+5,Math.Max(1,visible.Width-12),Math.Max(1,visible.Height-10));
                using(var labelFont=new Font(Font,FontStyle.Bold))
                using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter})
                using(var white=new SolidBrush(Color.FromArgb(245,255,255,255)))
                using(var purple=new SolidBrush(Color.FromArgb(112,74,170)))
                {
                    string label=L.T(placementLabel);
                    foreach(var offset in new[]{new PointF(-1,-1),new PointF(-1,1),new PointF(1,-1),new PointF(1,1)})
                    {var shadowBounds=labelBounds;shadowBounds.Offset(offset);g.DrawString(label,labelFont,white,shadowBounds,format);}
                    g.DrawString(label,labelFont,purple,labelBounds,format);
                }
            }
            finally {g.Restore(state);}
        }
        protected override void Dispose(bool disposing){if(disposing){dragTimer.Stop();dragTimer.Dispose();}base.Dispose(disposing);}
        protected override void OnClick(EventArgs e){if(!TextPlacement||Image==null)base.OnClick(e);}
        internal void TestDrag(Point a,Point b){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,a.X,a.Y,0));OnMouseMove(new MouseEventArgs(MouseButtons.Left,1,b.X,b.Y,0));OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,b.X,b.Y,0));}
        internal void TestBurst(Point a,Point b,int count){OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,a.X,a.Y,0));for(int i=1;i<=count;i++)OnMouseMove(new MouseEventArgs(MouseButtons.Left,1,a.X+(b.X-a.X)*i/count,a.Y+(b.Y-a.Y)*i/count,0));OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,b.X,b.Y,0));}
    }
}
