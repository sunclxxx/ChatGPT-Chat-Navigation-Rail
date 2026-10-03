using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
namespace LocalChatRail {
 static class RailUi {
  public static bool Dark=Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1))==0;
  public static Color Surface{get{return Dark?Color.FromArgb(36,36,36):Color.White;}}
  public static Color Ink{get{return Dark?Color.FromArgb(240,241,242):Color.FromArgb(26,28,31);}}
  public static Color MenuInk{get{return Dark?Color.FromArgb(219,221,224):Color.FromArgb(69,71,75);}}
  public static Color MenuHover{get{return Dark?Color.FromArgb(49,51,54):Color.FromArgb(245,245,246);}}
  public static Color MenuBorder{get{return Dark?Color.FromArgb(60,62,65):Color.FromArgb(235,236,238);}}
  public static Color Muted{get{return Dark?Color.FromArgb(170,172,175):Color.FromArgb(106,107,109);}}
  public static Color Hover{get{return Dark?Color.FromArgb(53,54,56):Color.FromArgb(244,244,245);}}
  public static Color Border{get{return Dark?Color.FromArgb(62,64,67):Color.FromArgb(228,228,229);}}
  // GDI+ uses an explicit CJK sans-serif instead of the native edit control's serif fallback.
  public static readonly Font BodyFont=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel);
  public static readonly Font MenuFont=(Font)SystemFonts.MenuFont.Clone();
  public static readonly Font HeadingFont=new Font("Microsoft YaHei UI",20,FontStyle.Bold,GraphicsUnit.Pixel);
  [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr h,int attr,ref int value,int size);
  public static void Frame(IntPtr h){try{int dark=Dark?1:0,round=2;DwmSetWindowAttribute(h,20,ref dark,4);DwmSetWindowAttribute(h,33,ref round,4);}catch{}}
  public static bool NativeCorners(IntPtr h){try{int round=2,none=unchecked((int)0xfffffffe);bool supported=DwmSetWindowAttribute(h,33,ref round,4)==0;DwmSetWindowAttribute(h,34,ref none,4);return supported;}catch{return false;}}
  public static GraphicsPath Rounded(Rectangle r,int radius){var p=new GraphicsPath();int d=radius*2;p.AddArc(r.Left,r.Top,d,d,180,90);p.AddArc(r.Right-d,r.Top,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.Left,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
  public static void Text(Graphics g,string text,Font font,Color color,RectangleF box,bool middle){g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;using(var brush=new SolidBrush(color))using(var format=new StringFormat{Alignment=StringAlignment.Near,LineAlignment=middle?StringAlignment.Center:StringAlignment.Near,Trimming=StringTrimming.EllipsisCharacter})g.DrawString(text,font,brush,box,format);}
  sealed class BorderlessMenu:ContextMenuStrip {protected override Padding DefaultPadding{get{return new Padding(0,4,0,4);}}protected override CreateParams CreateParams{get{var p=base.CreateParams;p.Style&=~0x00800000;p.ExStyle&=~0x00000200;p.ExStyle&=~0x00040000;p.ExStyle|=0x00000080;return p;}}}
  public static ContextMenuStrip Menu(){var menu=new BorderlessMenu{Font=MenuFont,ShowImageMargin=false,ShowCheckMargin=false,Padding=new Padding(0,4,0,4),AutoSize=false,BackColor=Surface,ForeColor=MenuInk,Renderer=new MenuPainter(),DropShadowEnabled=true};menu.ItemAdded+=(s,e)=>{e.Item.AutoSize=false;e.Item.Size=new Size(208,e.Item is ToolStripSeparator?9:28);e.Item.Margin=Padding.Empty;e.Item.Padding=Padding.Empty;int height=8;foreach(ToolStripItem row in menu.Items)height+=row is ToolStripSeparator?9:28;menu.Padding=new Padding(0,4,0,4);menu.Size=new Size(208,height);};menu.Opening+=(s,e)=>{menu.BackColor=Surface;menu.ForeColor=MenuInk;foreach(ToolStripItem item in menu.Items)item.ForeColor=MenuInk;var old=menu.Region;using(var shape=Rounded(new Rectangle(0,0,menu.Width,menu.Height),10))menu.Region=new Region(shape);if(old!=null)old.Dispose();Frame(menu.Handle);try{int none=unchecked((int)0xfffffffe);DwmSetWindowAttribute(menu.Handle,34,ref none,4);}catch{}};return menu;}
  sealed class MenuPainter:ToolStripProfessionalRenderer {
   protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(Surface);}
   protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){}
   protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){if(!e.Item.Selected)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Rounded(new Rectangle(0,2,e.ToolStrip.ClientSize.Width,e.Item.Height-4),4))using(var brush=new SolidBrush(MenuHover))e.Graphics.FillPath(brush,path);}
   protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){TextRenderer.DrawText(e.Graphics,e.Item.Text,MenuFont,new Rectangle(16,0,e.Item.Width-24,e.Item.Height),e.Item.Enabled?MenuInk:Muted,TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsTranslateTransform|TextFormatFlags.PreserveGraphicsClipping);string kind=Convert.ToString(e.Item.Tag);if(kind=="toggle"){var paintState=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;bool on=((ToolStripMenuItem)e.Item).Checked;var r=new Rectangle(e.Item.Width-40,(e.Item.Height-16)/2,30,16);using(var path=Rounded(r,8))using(var brush=new SolidBrush(on?MenuInk:MenuBorder))e.Graphics.FillPath(brush,path);using(var brush=new SolidBrush(on?Surface:Color.White))e.Graphics.FillEllipse(brush,r.X+(on?16:2),r.Y+2,12,12);e.Graphics.Restore(paintState);}}
   protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var pen=new Pen(MenuBorder))e.Graphics.DrawLine(pen,8,e.Item.Height/2,e.Item.Width-8,e.Item.Height/2);}
  }
  static void Icon(Graphics g,string kind,Point pos,Color color){g.SmoothingMode=SmoothingMode.AntiAlias;var state=g.Save();g.TranslateTransform(pos.X,pos.Y);using(var pen=new Pen(color,1.4f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}){if(kind=="toggle"){g.DrawEllipse(pen,1.5f,1.5f,13,13);g.DrawLine(pen,8,7,8,11);g.DrawEllipse(pen,7.6f,4.3f,.8f,.8f);}else if(kind=="refresh"){g.DrawArc(pen,2,2,12,12,35,280);g.DrawLines(pen,new[]{new PointF(11,1),new PointF(14,3),new PointF(11,5)});}else if(kind=="book"){g.DrawLines(pen,new[]{new PointF(1,2),new PointF(7,2),new PointF(8,4),new PointF(9,2),new PointF(15,2),new PointF(15,13),new PointF(9,13),new PointF(8,14),new PointF(7,13),new PointF(1,13),new PointF(1,2)});g.DrawLine(pen,8,4,8,14);}else{g.DrawArc(pen,2,2,12,12,-55,290);g.DrawLine(pen,8,0,8,7);}}g.Restore(state);}
 }
 class RailDialog:Form {
  string title,body;bool busy,closeHover,xHover;int scroll;
  Rectangle closeBounds,xBounds,bodyBounds;
  public bool Busy{set{busy=value;Invalidate();}}
  bool nativeCorners;
  protected override CreateParams CreateParams{get{var p=base.CreateParams;p.Style|=0x00040000;p.ExStyle|=0x00000080;return p;}}
  protected override void WndProc(ref Message m){if(m.Msg==0x83){m.Result=IntPtr.Zero;return;}if(m.Msg==0x84){base.WndProc(ref m);if(m.Result.ToInt32()>=10&&m.Result.ToInt32()<=17)m.Result=new IntPtr(1);return;}base.WndProc(ref m);}
  protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);nativeCorners=RailUi.NativeCorners(Handle);ApplyCorners();}
  [DllImport("user32.dll")]static extern bool ReleaseCapture();
  [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr h,int message,IntPtr w,IntPtr l);
  public RailDialog(string heading,string text){title=heading;body=text;Text="Chat Navigation Rail";Font=RailUi.BodyFont;AutoScaleMode=AutoScaleMode.Dpi;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;KeyPreview=true;DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);AccessibleName=heading;AccessibleRole=AccessibleRole.Dialog;LayoutCard();Shown+=(s,e)=>{var area=Screen.FromPoint(Cursor.Position).WorkingArea;Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);ApplyTheme();};}
  void ApplyCorners(){var old=Region;if(nativeCorners)Region=null;else using(var path=RailUi.Rounded(new Rectangle(0,0,ClientSize.Width,ClientSize.Height),12))Region=new Region(path);if(old!=null)old.Dispose();}
  void LayoutCard(){int height;using(var g=CreateGraphics())height=(int)Math.Ceiling(g.MeasureString(body,RailUi.BodyFont,400).Height);height=Math.Max(28,Math.Min(190,height+4));ClientSize=new Size(448,146+height);bodyBounds=new Rectangle(24,64,400,height);closeBounds=new Rectangle(ClientSize.Width-104,ClientSize.Height-60,80,36);xBounds=new Rectangle(ClientSize.Width-48,16,32,32);if(IsHandleCreated)ApplyCorners();}
  public void UpdateText(string text){if(body!=text){body=text;scroll=0;LayoutCard();}ApplyTheme();}
  public void SetTitle(string text){title=text;AccessibleName=text;Invalidate();}
  public void ApplyTheme(){BackColor=RailUi.Surface;if(IsHandleCreated)RailUi.Frame(Handle);Invalidate();}
  protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(RailUi.Surface);if(!nativeCorners)using(var pen=new Pen(RailUi.Border))using(var path=RailUi.Rounded(new Rectangle(0,0,ClientSize.Width-1,ClientSize.Height-1),12))g.DrawPath(pen,path);RailUi.Text(g,title,RailUi.HeadingFont,RailUi.Ink,new RectangleF(24,22,Width-88,28),false);var save=g.Save();g.SetClip(bodyBounds);RailUi.Text(g,body,RailUi.BodyFont,RailUi.Muted,new RectangleF(bodyBounds.X,bodyBounds.Y-scroll,bodyBounds.Width,4096),false);g.Restore(save);if(xHover&&!busy)using(var path=RailUi.Rounded(xBounds,8))using(var brush=new SolidBrush(RailUi.Hover))g.FillPath(brush,path);using(var pen=new Pen(RailUi.Muted,1.5f)){g.DrawLine(pen,xBounds.X+11,xBounds.Y+11,xBounds.X+21,xBounds.Y+21);g.DrawLine(pen,xBounds.X+21,xBounds.Y+11,xBounds.X+11,xBounds.Y+21);}var buttonColor=RailUi.Dark?Color.FromArgb(240,241,242):Color.FromArgb(26,28,31);if(closeHover&&!busy)buttonColor=RailUi.Dark?Color.FromArgb(212,214,217):Color.FromArgb(55,57,60);using(var path=RailUi.Rounded(closeBounds,8))using(var brush=new SolidBrush(busy?RailUi.Hover:buttonColor))g.FillPath(brush,path);TextRenderer.DrawText(g,"关闭",RailUi.BodyFont,closeBounds,busy?RailUi.Muted:(RailUi.Dark?Color.FromArgb(26,28,31):Color.White),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPadding);}
  protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);bool c=closeBounds.Contains(e.Location),x=xBounds.Contains(e.Location);if(c!=closeHover||x!=xHover){closeHover=c;xHover=x;Cursor=!busy&&(c||x)?Cursors.Hand:Cursors.Default;Invalidate();}}
  protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);closeHover=xHover=false;Invalidate();}
  protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left&&e.Y<60&&!xBounds.Contains(e.Location)){ReleaseCapture();SendMessage(Handle,0xa1,new IntPtr(2),IntPtr.Zero);}}
  protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(!busy&&e.Button==MouseButtons.Left&&(closeBounds.Contains(e.Location)||xBounds.Contains(e.Location)))Close();}
  protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);int extent;using(var g=CreateGraphics())extent=(int)g.MeasureString(body,RailUi.BodyFont,400).Height;scroll=Math.Max(0,Math.Min(Math.Max(0,extent-bodyBounds.Height),scroll-e.Delta/3));Invalidate();}
  protected override bool ProcessCmdKey(ref Message msg,Keys key){if(!busy&&(key==Keys.Escape||key==Keys.Enter)){Close();return true;}return base.ProcessCmdKey(ref msg,key);}
  protected override void OnFormClosing(FormClosingEventArgs e){if(busy&&e.CloseReason==CloseReason.UserClosing)e.Cancel=true;base.OnFormClosing(e);}
 }
}
