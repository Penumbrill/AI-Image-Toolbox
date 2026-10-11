using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QQImageSwitch
{
    static class SoftTheme
    {
        public static readonly Color Background=Color.FromArgb(246,244,251);
        public static GraphicsPath Round(RectangleF rectangle,float radius)
        {
            var path=new GraphicsPath();float diameter=Math.Max(1,Math.Min(radius*2,Math.Min(rectangle.Width,rectangle.Height)));
            path.AddArc(rectangle.X,rectangle.Y,diameter,diameter,180,90);
            path.AddArc(rectangle.Right-diameter,rectangle.Y,diameter,diameter,270,90);
            path.AddArc(rectangle.Right-diameter,rectangle.Bottom-diameter,diameter,diameter,0,90);
            path.AddArc(rectangle.X,rectangle.Bottom-diameter,diameter,diameter,90,90);path.CloseFigure();return path;
        }
        public static void Backdrop(Graphics g,Rectangle bounds)
        {
            if(bounds.Width<1||bounds.Height<1)return;
            using(var gradient=new LinearGradientBrush(bounds,Color.FromArgb(248,244,254),Color.FromArgb(238,246,254),35f))g.FillRectangle(gradient,bounds);
            using(var glow=new SolidBrush(Color.FromArgb(32,235,189,217)))g.FillEllipse(glow,-bounds.Width/5,-bounds.Height/3,bounds.Width*3/4,bounds.Height*4/5);
            using(var glow=new SolidBrush(Color.FromArgb(24,165,219,233)))g.FillEllipse(glow,bounds.Width/2,bounds.Height/2,bounds.Width*3/4,bounds.Height);
        }
        public static void Card(Graphics g,Rectangle bounds,Color background)
        {
            if(bounds.Width<4||bounds.Height<4)return;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shadow=Round(new RectangleF(3,4,bounds.Width-7,bounds.Height-5),20))using(var ink=new SolidBrush(Color.FromArgb(14,118,100,155)))g.FillPath(ink,shadow);
            var rectangle=new RectangleF(1,1,bounds.Width-6,bounds.Height-7);
            using(var shape=Round(rectangle,20))
            using(var frost=new LinearGradientBrush(rectangle,Color.FromArgb(254,254,255),Color.FromArgb(244,247,253),70f))
            using(var border=new Pen(Color.FromArgb(255,255,255),1.5f))
            {g.FillPath(frost,shape);g.DrawPath(border,shape);}
        }
    }
    class SoftPanel : Panel
    {
        Bitmap backdrop;
        Color backdropColor;
        public SoftPanel(){DoubleBuffered=true;}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if(Width<1||Height<1)return;
            if(BackColor==SoftTheme.Background||BackColor==Color.FromArgb(245,247,252)||BackColor==Color.White)
            {
                if(backdrop==null||backdrop.Size!=ClientSize||backdropColor!=BackColor)
                {
                    if(backdrop!=null)backdrop.Dispose();backdrop=new Bitmap(Width,Height);backdropColor=BackColor;
                    using(var g=Graphics.FromImage(backdrop))
                    {
                        if(BackColor==Color.White)using(var brush=new LinearGradientBrush(ClientRectangle,Color.FromArgb(254,253,255),Color.FromArgb(245,248,254),75f))g.FillRectangle(brush,ClientRectangle);
                        else SoftTheme.Backdrop(g,ClientRectangle);
                    }
                }
                e.Graphics.DrawImageUnscaled(backdrop,0,0);
            }
            else base.OnPaintBackground(e);
        }
        protected override void Dispose(bool disposing){if(disposing&&backdrop!=null)backdrop.Dispose();base.Dispose(disposing);}
    }
    class SoftScrollPanel : SoftPanel
    {
        protected override void OnScroll(ScrollEventArgs e)
        {
            base.OnScroll(e);
            // Native scrolling copies pixels. Repaint glass corners against the new
            // viewport background, retaining the image canvases' cached pixels.
            Invalidate(true);RefreshBackgrounds(this);
        }
        static void RefreshBackgrounds(Control root)
        {
            foreach(Control child in root.Controls)
            {
                if(!child.Visible||!child.Bounds.IntersectsWith(root.ClientRectangle))continue;
                if(child is SoftTable||child is Label||child is ButtonBase||child is SoftCombo||child is SoftInput)child.Invalidate();
                if(child is TableLayoutPanel||child is FlowLayoutPanel)RefreshBackgrounds(child);
            }
        }
    }
    // Buffer the tool subtree together, including native child controls. The OS
    // owns presentation; this works without a particular GPU or vendor driver.
    class ToolPageHost : SoftPanel
    {
        // Each managed surface buffers its own painting. WS_EX_COMPOSITED on the
        // whole subtree can retain stale child pixels after native scrolling,
        // making a painted button appear over a different live input control.
    }
    class SoftTable : TableLayoutPanel
    {
        Bitmap backdrop;
        Rectangle oldBounds;
        public SoftTable(){DoubleBuffered=true;}
        bool wrappingLabels;
        protected override void OnLayout(LayoutEventArgs e)
        {
            if(!wrappingLabels&&ColumnCount==1&&!IsDisposed&&!Disposing)
            {
                wrappingLabels=true;
                try{foreach(Control child in Controls)if(child is Label&&(child.Tag as string)!="fixedwrap"&&(child.Tag as string)!="singleline")
                {var maximum=new Size(Math.Max(80,ClientSize.Width-Padding.Horizontal-child.Margin.Horizontal-8),0);if(child.MaximumSize!=maximum)child.MaximumSize=maximum;}}
                finally{wrappingLabels=false;}
            }
            base.OnLayout(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if(BackColor==Color.White&&Padding.Horizontal>0)
            {
                if(Width<1||Height<1)return;
                if(backdrop==null||backdrop.Size!=ClientSize)
                {if(backdrop!=null)backdrop.Dispose();backdrop=new Bitmap(Width,Height,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);using(var g=Graphics.FromImage(backdrop)){g.Clear(Color.Transparent);SoftTheme.Card(g,ClientRectangle,BackColor);}}
                if(Parent!=null){var state=e.Graphics.Save();e.Graphics.TranslateTransform(-Left,-Top);InvokePaintBackground(Parent,new PaintEventArgs(e.Graphics,new Rectangle(Left,Top,Width,Height)));e.Graphics.Restore(state);}else e.Graphics.Clear(SoftTheme.Background);
                e.Graphics.DrawImageUnscaled(backdrop,0,0);
            }
            else base.OnPaintBackground(e);
        }
        void RepaintOldBounds(){if(Visible&&BackColor==Color.White&&Padding.Horizontal>0&&Parent!=null&&!Parent.IsDisposed)Parent.Invalidate(Rectangle.Union(oldBounds,Bounds),false);oldBounds=Bounds;}
        protected override void OnLocationChanged(EventArgs e){base.OnLocationChanged(e);RepaintOldBounds();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);RepaintOldBounds();Invalidate();}
        protected override void Dispose(bool disposing){if(disposing&&backdrop!=null)backdrop.Dispose();base.Dispose(disposing);}
    }
    // A vertical, single-column form needs only one measurement per child.
    // TableLayoutPanel's general grid measurement repeatedly walks nested forms.
    class SoftColumn : SoftTable
    {
        readonly System.Collections.Generic.Dictionary<int,System.Collections.Generic.KeyValuePair<int,Size>> measured=new System.Collections.Generic.Dictionary<int,System.Collections.Generic.KeyValuePair<int,Size>>();
        static int Fingerprint(Control control)
        {
            unchecked
            {
                if(control.IsDisposed||control.Disposing)return 0;
                string caption=control is Label||control is ButtonBase?control.Text:string.Empty;
                int hash=caption.GetHashCode()*31+control.Font.GetHashCode();hash=hash*31+control.Margin.GetHashCode();hash=hash*31+control.Padding.GetHashCode();hash=hash*31+control.MaximumSize.GetHashCode();hash=hash*31+control.MinimumSize.GetHashCode();hash=hash*31+(control.Visible?1:0);
                if(!control.AutoSize)hash=hash*31+control.Height;
                // Native numeric/edit controls manage private child windows. Reading their
                // Text while disposing can re-enter validation and recreate handles.
                if(!(control is UpDownBase)&&!(control is TextBoxBase)&&!(control is ComboBox))
                    foreach(Control child in control.Controls)hash=hash*31+Fingerprint(child);return hash;
            }
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            if(IsDisposed||Disposing)return Size;
            if(!AutoSize||ColumnCount!=1)return base.GetPreferredSize(proposedSize);
            int width=Math.Max(100,proposedSize.Width>0?proposedSize.Width:Width),height=Padding.Vertical;
            int stamp=Fingerprint(this);System.Collections.Generic.KeyValuePair<int,Size> cached;
            if(measured.TryGetValue(width,out cached)&&cached.Key==stamp)return cached.Value;
            foreach(Control child in Controls)
            {
                if(Visible&&!child.Visible)continue;
                int available=Math.Max(80,width-Padding.Horizontal-child.Margin.Horizontal);
                int needed=child.AutoSize?child.GetPreferredSize(new Size(available,0)).Height:Math.Max(child.Height,child.MinimumSize.Height);
                height+=needed+child.Margin.Vertical;
            }
            var result=new Size(width,Math.Max(MinimumSize.Height,height));if(measured.Count>8)measured.Clear();measured[width]=new System.Collections.Generic.KeyValuePair<int,Size>(stamp,result);return result;
        }
    }
    class CompactFooter : SoftTable
    {
        bool measuring,reflowQueued;
        public CompactFooter()
        {
            ColumnCount=1;RowCount=0;AutoSize=false;Dock=DockStyle.Bottom;AutoScroll=true;Margin=new Padding(0);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if(measuring){base.OnLayout(e);return;}
            measuring=true;bool changed=false;
            try
            {
                if(Visible)
                {
                    int width=Math.Max(100,ClientSize.Width-Padding.Horizontal-(VerticalScroll.Visible?SystemInformation.VerticalScrollBarWidth:0));
                    int height=Padding.Vertical;
                    for(int row=0;row<RowCount;row++)
                    {
                        int needed=0;
                        foreach(Control child in Controls)if(child.Visible&&GetRow(child)==row)
                            needed=Math.Max(needed,child.GetPreferredSize(new Size(Math.Max(1,width-child.Margin.Horizontal),0)).Height+child.Margin.Vertical);
                        if(row<RowStyles.Count){var style=RowStyles[row];if(style.SizeType!=SizeType.Absolute)style.SizeType=SizeType.Absolute;if(style.Height!=needed)style.Height=needed;}
                        height+=needed;
                    }
                    int limit=Parent==null||Parent.ClientSize.Height<1?height:Math.Max(140,Parent.ClientSize.Height*2/5);
                    int fit=Math.Min(HeightResize.Resolve(this,height),limit);if(Height!=fit){Height=fit;changed=true;}
                    if(Parent!=null&&Dock==DockStyle.Bottom){int top=Parent.ClientSize.Height-Parent.Padding.Bottom-Height;if(Top!=top)Top=top;}
                    bool overflow=height>fit;if(AutoScroll!=overflow){AutoScroll=overflow;changed=true;}
                    var extent=new Size(0,height>fit?height:0);if(AutoScrollMinSize!=extent)AutoScrollMinSize=extent;
                }
                base.OnLayout(e);
            }
            finally{measuring=false;}
            // A footer can shrink while its parent is docking children. Reflow the fill sibling too.
            if(changed)ReflowParent();
        }
        void ReflowParent()
        {
            if(reflowQueued||!IsHandleCreated||IsDisposed||Disposing)return;
            reflowQueued=true;BeginInvoke((MethodInvoker)delegate
            {
                reflowQueued=false;if(IsDisposed||Disposing||Parent==null||Parent.IsDisposed)return;
                Parent.PerformLayout(this,"Bounds");
            });
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);ReflowParent();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(Visible){PerformLayout();ReflowParent();}}
    }
    class SoftFlow : FlowLayoutPanel {public SoftFlow(){DoubleBuffered=true;}}
    class SoftNumber : NumericUpDown
    {
        int wheelDelta;
        void AdjustWheel(int delta)
        {
            if(!Enabled)return;
            wheelDelta+=delta;
            while(wheelDelta>=120){wheelDelta-=120;Value=Increment>=Maximum-Value?Maximum:Value+Increment;}
            while(wheelDelta<=-120){wheelDelta+=120;Value=Increment>=Value-Minimum?Minimum:Value-Increment;}
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var handled=e as HandledMouseEventArgs;if(handled!=null)handled.Handled=true;AdjustWheel(e.Delta);
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x20a){AdjustWheel((short)((long)message.WParam>>16));message.Result=IntPtr.Zero;return;}
            base.WndProc(ref message);
        }
    }
    static class QueueWheel
    {
        // A native list consumes the wheel even when there is nothing left to scroll.
        // Forward only at a boundary, retaining native scrolling inside the queue.
        public static bool Forward(Control source,ref Message message,bool canScroll)
        {
            if(message.Msg!=0x20a||canScroll||((long)message.WParam&12)!=0)return false;
            int delta=(short)((long)message.WParam>>16);if(delta==0)return false;
            for(Control parent=source.Parent;parent!=null;parent=parent.Parent)
            {
                var scroll=parent as ScrollableControl;
                if(scroll==null||!scroll.AutoScroll||!scroll.VerticalScroll.Visible)continue;
                var bar=scroll.VerticalScroll;
                int maximum=Math.Max(bar.Minimum,bar.Maximum-bar.LargeChange+1);
                if(delta>0?bar.Value<=bar.Minimum:bar.Value>=maximum)continue;
                message.Result=SendMessage(scroll.Handle,message.Msg,message.WParam,message.LParam);return true;
            }
            return false;
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wparam,IntPtr lparam);
    }
    class SoftGrid : DataGridView
    {
        readonly System.Collections.Generic.Dictionary<DataGridViewColumn,string> captions=new System.Collections.Generic.Dictionary<DataGridViewColumn,string>();
        public SoftGrid(){DoubleBuffered=true;ColumnAdded+=delegate(object sender,DataGridViewColumnEventArgs e){captions[e.Column]=e.Column.HeaderText;e.Column.HeaderText=L.T(e.Column.HeaderText);};ColumnRemoved+=delegate(object sender,DataGridViewColumnEventArgs e){captions.Remove(e.Column);};CellFormatting+=delegate(object sender,DataGridViewCellFormattingEventArgs e){if(e.Value is string&&(Columns[e.ColumnIndex].Name=="state"||Columns[e.ColumnIndex].Name=="cover")){e.Value=L.T((string)e.Value);}};L.Changed+=LanguageChanged;}
        void LanguageChanged(){foreach(var p in captions)p.Key.HeaderText=L.T(p.Value);Invalidate();}
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x20a)
            {
                int delta=(short)((long)message.WParam>>16);bool canScroll=false;
                foreach(Control child in Controls)
                {
                    var bar=child as VScrollBar;if(bar==null||!bar.Visible)continue;
                    int maximum=Math.Max(bar.Minimum,bar.Maximum-bar.LargeChange+1);
                    canScroll=delta>0?bar.Value>bar.Minimum:bar.Value<maximum;break;
                }
                if(QueueWheel.Forward(this,ref message,canScroll))return;
            }
            base.WndProc(ref message);
        }
        protected override void Dispose(bool disposing){if(disposing)L.Changed-=LanguageChanged;base.Dispose(disposing);}
    }
    class SoftButton : Button
    {
        bool hovered,pressed;
        ToolTip captionTip;
        public int NavigationIcon=-1;
        public string HoverText {get{if(!string.IsNullOrWhiteSpace(AccessibleDescription))return L.T(AccessibleDescription);if(string.IsNullOrWhiteSpace(Text))return L.T(AccessibleName??"按钮");return Text.EndsWith("…")||Text.EndsWith("...")?Text.TrimEnd('…','.')+L.T("（打开选择窗口）"):Text;}}
        public SoftButton(){DoubleBuffered=true;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;}
        protected override void OnMouseEnter(EventArgs e){if(captionTip==null)captionTip=new ToolTip {InitialDelay=350,ReshowDelay=100,AutoPopDelay=15000,ShowAlways=true};captionTip.SetToolTip(this,HoverText);hovered=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){if(captionTip!=null)captionTip.Hide(this);hovered=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);if(captionTip!=null)captionTip.SetToolTip(this,HoverText);}
        protected override void Dispose(bool disposing){if(disposing&&captionTip!=null){captionTip.Dispose();captionTip=null;}base.Dispose(disposing);}
        internal void TestHint(){OnMouseEnter(EventArgs.Empty);if(captionTip.GetToolTip(this)!=HoverText||string.IsNullOrWhiteSpace(HoverText))throw new Exception("Button tooltip missing");OnMouseLeave(EventArgs.Empty);}
        protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}
        protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            if(Parent!=null)
            {
                var state=g.Save();g.TranslateTransform(-Left,-Top);InvokePaintBackground(Parent,new PaintEventArgs(g,new Rectangle(Left,Top,Width,Height)));g.Restore(state);
            }
            else g.Clear(SoftTheme.Background);
            bool primary=BackColor==Ui.Purple;Color top=BackColor,bottom=BackColor;
            if(primary){top=Color.FromArgb(168,145,228);bottom=Color.FromArgb(139,118,210);}
            else if(BackColor==Color.White){top=Color.FromArgb(253,251,255);bottom=Color.FromArgb(240,239,250);}
            if(!Enabled){top=Color.FromArgb(242,240,247);bottom=top;}
            if(hovered&&Enabled){top=ControlPaint.Light(top,.08f);bottom=ControlPaint.Light(bottom,.08f);}
            if(pressed&&Enabled){top=ControlPaint.Dark(top,.06f);bottom=ControlPaint.Dark(bottom,.06f);}
            var rectangle=new RectangleF(1,1,Math.Max(1,Width-4),Math.Max(1,Height-5));
            using(var shadow=SoftTheme.Round(new RectangleF(2,3,Math.Max(1,Width-4),Math.Max(1,Height-4)),16))using(var brush=new SolidBrush(Color.FromArgb(15,107,88,152)))g.FillPath(brush,shadow);
            using(var path=SoftTheme.Round(rectangle,16))using(var brush=new LinearGradientBrush(rectangle,top,bottom,90f))using(var border=new Pen(primary?Color.FromArgb(190,174,234):Color.FromArgb(227,225,241)))
            {g.FillPath(brush,path);g.DrawPath(border,path);}
            if(NavigationIcon>=0)
            {
                float unit=Math.Min(Font.Height*.66f,Math.Min(Width,Height)-12),x=Text.Length==0?(Width-unit)/2f:Padding.Left-unit-10,y=(Height-unit)/2f;
                using(var pen=new Pen(primary?Color.White:Color.FromArgb(153,136,192),Math.Max(1.6f,unit*.095f)){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})using(var fill=new SolidBrush(primary?Color.White:Color.FromArgb(153,136,192)))
                {
                    if(NavigationIcon==0){g.DrawRectangle(pen,x+3,y,unit-3,unit-3);g.DrawRectangle(pen,x,y+4,unit-3,unit-3);}
                    else if(NavigationIcon==9){g.DrawEllipse(pen,x,y,unit,unit);g.FillPie(fill,x,y,unit,unit,90,180);}
                    else if(NavigationIcon==8){for(int row=0;row<3;row++)for(int col=0;col<3;col++)g.FillRectangle(fill,x+col*unit*.35f,y+row*unit*.35f,unit*.23f,unit*.23f);}
                    else if(NavigationIcon==1)g.FillPolygon(fill,new[]{new PointF(x+2,y),new PointF(x+unit,y+unit/2),new PointF(x+2,y+unit)});
                    else if(NavigationIcon==2)
                    {
                        // Continuous undo stroke: a clearly connected left arrow and a rounded return curve.
                        using(var path=new GraphicsPath())
                        {
                            path.AddLine(x+unit*.09f,y+unit*.32f,x+unit*.52f,y+unit*.32f);
                            path.AddBezier(x+unit*.52f,y+unit*.32f,x+unit*.84f,y+unit*.32f,x+unit*.92f,y+unit*.44f,x+unit*.92f,y+unit*.58f);
                            path.AddBezier(x+unit*.92f,y+unit*.58f,x+unit*.92f,y+unit*.77f,x+unit*.77f,y+unit*.88f,x+unit*.53f,y+unit*.88f);
                            path.AddLine(x+unit*.53f,y+unit*.88f,x+unit*.26f,y+unit*.88f);g.DrawPath(pen,path);
                        }
                        g.DrawLines(pen,new[]{new PointF(x+unit*.31f,y+unit*.10f),new PointF(x+unit*.09f,y+unit*.32f),new PointF(x+unit*.31f,y+unit*.54f)});
                    }
                    else if(NavigationIcon==3){g.DrawLine(pen,x+3,y,x+unit,y+unit);g.DrawLine(pen,x,y+unit,x+unit,y);g.DrawEllipse(pen,x,y,unit,unit);}
                    else if(NavigationIcon==11){g.DrawRectangle(pen,x,y,unit,unit);g.DrawLine(pen,x+unit*.5f,y,x+unit*.5f,y+unit);g.DrawLine(pen,x+unit*.12f,y+unit*.3f,x+unit*.38f,y+unit*.3f);g.DrawLine(pen,x+unit*.62f,y+unit*.3f,x+unit*.88f,y+unit*.3f);}
                    else if(NavigationIcon==10){foreach(var r in new[]{new RectangleF(x,y,unit*.4f,unit*.4f),new RectangleF(x+unit*.6f,y,unit*.4f,unit*.4f),new RectangleF(x,y+unit*.6f,unit*.4f,unit*.4f)}){g.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);g.FillRectangle(fill,r.X+r.Width*.3f,r.Y+r.Height*.3f,r.Width*.4f,r.Height*.4f);}g.FillRectangle(fill,x+unit*.6f,y+unit*.6f,unit*.18f,unit*.18f);g.FillRectangle(fill,x+unit*.82f,y+unit*.82f,unit*.18f,unit*.18f);}
                    else if(NavigationIcon==4){for(int row=0;row<2;row++)for(int col=0;col<2;col++)g.FillRectangle(fill,x+col*unit*.58f,y+row*unit*.58f,unit*.38f,unit*.38f);}
                    else if(NavigationIcon==5){g.DrawRectangle(pen,x,y,unit,unit);for(int row=0;row<3;row++)g.DrawLine(pen,x+3,y+3+row*unit*.25f,x+unit-3,y+3+row*unit*.25f);}
                    else if(NavigationIcon==7){g.DrawRectangle(pen,x,y,unit,unit);g.DrawLine(pen,x,y+unit,x+unit*.5f,y+unit*.45f);g.DrawLine(pen,x+unit*.5f,y+unit*.45f,x+unit,y+unit);g.FillEllipse(fill,x+unit*.64f,y+unit*.16f,unit*.18f,unit*.18f);}
                    else{g.DrawEllipse(pen,x,y,unit,unit);g.DrawEllipse(pen,x+unit*.3f,y+unit*.3f,unit*.4f,unit*.4f);}
                }
            }
            var text=new Rectangle(Padding.Left,Padding.Top,Math.Max(1,Width-Padding.Horizontal),Math.Max(1,Height-Padding.Vertical));
            var flags=TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
            flags|=TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter;
            TextRenderer.DrawText(g,Text,Font,text,Enabled?ForeColor:Color.FromArgb(157,151,173),flags);
            if(Focused&&ShowFocusCues)using(var path=SoftTheme.Round(new RectangleF(4,4,Math.Max(1,Width-10),Math.Max(1,Height-11)),13))using(var pen=new Pen(Color.FromArgb(151,130,211)){DashStyle=DashStyle.Dot})g.DrawPath(pen,path);
        }
    }
    class SoftInput : UserControl
    {
        readonly TextBox input;
        public SoftInput()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            BackColor=Color.Transparent;Height=42;MinimumSize=new Size(100,40);
            input=new TextBox {BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(254,253,255)};Controls.Add(input);
            input.TextChanged+=delegate{OnTextChanged(EventArgs.Empty);};Click+=delegate{input.Focus();};
        }
        public override string Text {get{return input==null?base.Text:input.Text;}set{if(input==null)base.Text=value;else input.Text=value;}}
        public int MaxLength {get{return input.MaxLength;}set{input.MaxLength=value;}}
        public bool Multiline {get{return input.Multiline;}set{input.Multiline=value;input.WordWrap=value;input.AcceptsReturn=false;input.ScrollBars=value?ScrollBars.Vertical:ScrollBars.None;if(value){Height=88;MinimumSize=new Size(100,88);}PerformLayout();}}
        public void FocusText(){input.Focus();input.SelectionStart=0;input.SelectionLength=0;input.ScrollToCaret();}
        protected override void OnLayout(LayoutEventArgs e)
        {base.OnLayout(e);if(input!=null){int pad=Math.Max(12,Font.Height/2);input.SetBounds(pad,input.Multiline?10:Math.Max(4,(Height-input.PreferredHeight)/2),Math.Max(1,Width-pad*2),input.Multiline?Math.Max(24,Height-20):input.PreferredHeight);}}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(input!=null){input.Font=Font;PerformLayout();}}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=SoftTheme.Round(new RectangleF(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3)),14))
            using(var fill=new SolidBrush(Color.FromArgb(254,253,255)))using(var pen=new Pen(Color.FromArgb(226,221,240)))
            {g.FillPath(fill,path);g.DrawPath(pen,path);}
        }
    }
    class SoftCombo : UserControl
    {
        readonly System.Collections.Generic.List<Control> owners=new System.Collections.Generic.List<Control>();
        public readonly System.Collections.Generic.List<object> Items=new System.Collections.Generic.List<object>();
        int selected=-1;
        ContextMenuStrip menu;
        public int VisibleRows;
        ToolStripDropDown scrollingMenu;
        SoftList choices;
        public ComboBoxStyle DropDownStyle {get{return ComboBoxStyle.DropDownList;}set{}}
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get{return selected;}
            set{if(value<-1||value>=Items.Count)throw new ArgumentOutOfRangeException("value");if(value==selected)return;selected=value;Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);}
        }
        public override string Text {get{return Items!=null&&selected>=0&&selected<Items.Count?Items[selected].ToString():"";}set{}}
        public SoftCombo()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            BackColor=Color.Transparent;ForeColor=Ui.Ink;Height=42;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.ComboBox;
            Click+=delegate{ShowChoices();};KeyDown+=delegate(object sender,KeyEventArgs e){if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter||e.KeyCode==Keys.Down||e.KeyCode==Keys.F4){ShowChoices();e.Handled=true;e.SuppressKeyPress=true;}};
            ParentChanged+=delegate{WatchOwners();};VisibleChanged+=OwnerMoved;EnabledChanged+=OwnerMoved;
        }
        void WatchOwners()
        {
            foreach(var owner in owners){owner.LocationChanged-=OwnerMoved;owner.SizeChanged-=OwnerMoved;var scroll=owner as ScrollableControl;if(scroll!=null)scroll.Scroll-=OwnerScrolled;}owners.Clear();
            for(Control owner=Parent;owner!=null;owner=owner.Parent){owners.Add(owner);owner.LocationChanged+=OwnerMoved;owner.SizeChanged+=OwnerMoved;var scroll=owner as ScrollableControl;if(scroll!=null)scroll.Scroll+=OwnerScrolled;}
        }
        void OwnerMoved(object sender,EventArgs e){CloseChoices();}
        void OwnerScrolled(object sender,ScrollEventArgs e){CloseChoices();}
        internal void CloseChoices(){if(menu!=null&&menu.Visible)menu.Close();if(scrollingMenu!=null&&scrollingMenu.Visible)scrollingMenu.Close();}
        void ShowChoices()
        {
            if(!Enabled||Items.Count==0||IsDisposed)return;
            WatchOwners();
            if(VisibleRows>0){ShowScrollingChoices();return;}
            if(menu==null)menu=new ContextMenuStrip {BackColor=Color.FromArgb(252,250,255),ForeColor=Ui.Ink,ShowImageMargin=false};
            if(menu.Visible)return;menu.Font=Font;
            while(menu.Items.Count>0){var old=menu.Items[0];menu.Items.RemoveAt(0);old.Dispose();}
            for(int i=0;i<Items.Count;i++)
            {
                int index=i;var item=new ToolStripMenuItem(L.T(Items[i].ToString())){Checked=i==selected,Padding=new Padding(8,5,8,5)};
                item.Click+=delegate{menu.Close(ToolStripDropDownCloseReason.ItemClicked);Focus();SelectedIndex=index;};menu.Items.Add(item);
            }
            menu.Show(this,new Point(0,Height-1));
        }
        void ShowScrollingChoices()
        {
            if(scrollingMenu==null)
            {
                choices=new SoftList {IntegralHeight=false,BorderStyle=BorderStyle.None,BackColor=Color.FromArgb(253,251,255)};
                choices.MouseClick+=delegate{CommitScrollingChoice();};choices.KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space){CommitScrollingChoice();e.Handled=true;}else if(e.KeyCode==Keys.Escape)scrollingMenu.Close();};
                scrollingMenu=new ToolStripDropDown {AutoSize=false,Padding=new Padding(4),BackColor=Color.FromArgb(253,251,255)};
                scrollingMenu.Items.Add(new ToolStripControlHost(choices){AutoSize=false,Margin=new Padding(0),Padding=new Padding(0)});
            }
            choices.BeginUpdate();choices.Font=Font;choices.Items.Clear();choices.Items.AddRange(Items.ToArray());choices.SelectedIndex=selected;
            choices.Size=new Size(Math.Max(160,Width),choices.ItemHeight*Math.Min(VisibleRows,Items.Count)+2);choices.EndUpdate();
            scrollingMenu.Items[0].Size=choices.Size;scrollingMenu.Size=new Size(choices.Width+8,choices.Height+8);
            scrollingMenu.Show(this,new Point(0,Height-1));choices.Focus();
        }
        void CommitScrollingChoice(){if(choices.SelectedIndex<0)return;int index=choices.SelectedIndex;scrollingMenu.Close(ToolStripDropDownCloseReason.ItemClicked);Focus();SelectedIndex=index;}
        internal bool PopupVisible {get{return menu!=null&&menu.Visible||scrollingMenu!=null&&scrollingMenu.Visible;}}
        internal void TestOpen(){ShowChoices();Application.DoEvents();if(!PopupVisible)throw new Exception("Choice popup did not open");}
        internal void TestChoice(int index)
        {ShowChoices();Application.DoEvents();if(VisibleRows>0){choices.SelectedIndex=index;CommitScrollingChoice();}else{((ToolStripMenuItem)menu.Items[index]).PerformClick();menu.Close(ToolStripDropDownCloseReason.ItemClicked);}Application.DoEvents();if(selected!=index)throw new Exception("Dropdown choice did not apply");}
        protected override void Dispose(bool disposing){if(disposing){foreach(var owner in owners){owner.LocationChanged-=OwnerMoved;owner.SizeChanged-=OwnerMoved;var scroll=owner as ScrollableControl;if(scroll!=null)scroll.Scroll-=OwnerScrolled;}owners.Clear();if(menu!=null){menu.Dispose();menu=null;}if(scrollingMenu!=null){scrollingMenu.Dispose();scrollingMenu=null;}}base.Dispose(disposing);}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Height=Math.Max(36,Font.Height+16);}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=SoftTheme.Round(new RectangleF(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3)),12))using(var fill=new SolidBrush(Color.FromArgb(253,252,255)))using(var pen=new Pen(Color.FromArgb(226,221,240)))
            {g.FillPath(fill,path);g.DrawPath(pen,path);}
            TextRenderer.DrawText(g,(Tag as string)=="raw"?Text:L.T(Text),Font,new Rectangle(12,0,Math.Max(1,Width-38),Height),Ui.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            using(var pen=new Pen(Ui.Muted,1.5f)){int x=Width-18,y=Height/2;g.DrawLines(pen,new[]{new Point(x-4,y-2),new Point(x,y+2),new Point(x+4,y-2)});}
        }
    }
    class SoftCheck : CheckBox
    {
        public SoftCheck(){BackColor=Color.Transparent;ForeColor=Ui.Ink;}
        public override Size GetPreferredSize(Size proposedSize){var size=base.GetPreferredSize(proposedSize);size.Width+=Math.Max(6,Font.Height/4);return size;}
        protected override void OnPaint(PaintEventArgs e)
        {
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            if(Parent!=null){var state=g.Save();g.TranslateTransform(-Left,-Top);InvokePaintBackground(Parent,new PaintEventArgs(g,new Rectangle(Left,Top,Width,Height)));g.Restore(state);}
            int side=Math.Max(14,(int)(Font.Height*.7f)),y=(Height-side)/2;
            using(var path=SoftTheme.Round(new RectangleF(1,y,side,side),5))using(var fill=new SolidBrush(Checked?Ui.Purple:Color.FromArgb(253,252,255)))using(var border=new Pen(Color.FromArgb(203,192,230)))
            {g.FillPath(fill,path);g.DrawPath(border,path);}
            if(Checked)using(var pen=new Pen(Color.White,2){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLines(pen,new[]{new PointF(4,y+side*.5f),new PointF(7,y+side*.75f),new PointF(side-2,y+side*.25f)});
            TextRenderer.DrawText(g,Text,Font,new Rectangle(side+7,0,Math.Max(1,Width-side-7),Height),Enabled?ForeColor:Ui.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        }
    }
    class SoftList : ListBox
    {
        public SoftList(){DrawMode=DrawMode.OwnerDrawFixed;ItemHeight=Font.Height+8;}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);ItemHeight=Font.Height+8;}
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x20a)
            {
                int delta=(short)((long)message.WParam>>16);
                int visible=Math.Max(1,ClientSize.Height/Math.Max(1,ItemHeight));
                bool canScroll=Items.Count>visible&&(delta>0?TopIndex>0:TopIndex<Items.Count-visible);
                if(QueueWheel.Forward(this,ref message,canScroll))return;
            }
            base.WndProc(ref message);
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if(e.Index<0)return;bool selected=(e.State&DrawItemState.Selected)!=0;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var fill=new SolidBrush(BackColor))e.Graphics.FillRectangle(fill,e.Bounds);
            if(selected)using(var shape=SoftTheme.Round(new RectangleF(e.Bounds.X+2,e.Bounds.Y+1,e.Bounds.Width-5,e.Bounds.Height-2),10))using(var fill=new SolidBrush(Color.FromArgb(234,226,250)))e.Graphics.FillPath(fill,shape);
            TextRenderer.DrawText(e.Graphics,L.T(Items[e.Index].ToString()),Font,new Point(e.Bounds.X+12,e.Bounds.Y+(e.Bounds.Height-Font.Height)/2),Ui.Ink,TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
        }
    }
}
