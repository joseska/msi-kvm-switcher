using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Drawing.Imaging;using System.IO;
class M{
static Bitmap Draw(int s){
 var b=new Bitmap(s,s,PixelFormat.Format32bppArgb);
 using(var g=Graphics.FromImage(b)){
  g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.Transparent);
  float r=s*0.22f;var rc=new RectangleF(0,0,s-1,s-1);
  var p=new GraphicsPath();p.AddArc(rc.X,rc.Y,r*2,r*2,180,90);p.AddArc(rc.Right-r*2,rc.Y,r*2,r*2,270,90);p.AddArc(rc.Right-r*2,rc.Bottom-r*2,r*2,r*2,0,90);p.AddArc(rc.X,rc.Bottom-r*2,r*2,r*2,90,90);p.CloseFigure();
  using(var br=new LinearGradientBrush(rc,Color.FromArgb(255,0,150,255),Color.FromArgb(255,20,50,160),90f))g.FillPath(br,p);
  float w=Math.Max(1.6f,s*0.10f);
  using(var pen=new Pen(Color.White,w)){pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;
   // flecha superior hacia la derecha y flecha inferior hacia la izquierda
   float x0=s*0.20f,x1=s*0.80f,y0=s*0.36f,y1=s*0.64f,h=s*0.13f;
   g.DrawLine(pen,x0,y0,x1,y0);g.DrawLine(pen,x1-h,y0-h,x1,y0);g.DrawLine(pen,x1-h,y0+h,x1,y0);
   g.DrawLine(pen,x0,y1,x1,y1);g.DrawLine(pen,x0+h,y1-h,x0,y1);g.DrawLine(pen,x0+h,y1+h,x0,y1);}
 }
 return b;}
static void Main(){
 int[] sizes={16,24,32,48,64,256};var pngs=new byte[sizes.Length][];
 for(int i=0;i<sizes.Length;i++){using(var b=Draw(sizes[i]))using(var ms=new MemoryStream()){b.Save(ms,ImageFormat.Png);pngs[i]=ms.ToArray();}}
 using(var f=new BinaryWriter(File.Create("kvmswitch.ico"))){
  f.Write((short)0);f.Write((short)1);f.Write((short)sizes.Length);int off=6+16*sizes.Length;
  for(int i=0;i<sizes.Length;i++){int s=sizes[i];f.Write((byte)(s==256?0:s));f.Write((byte)(s==256?0:s));f.Write((byte)0);f.Write((byte)0);f.Write((short)1);f.Write((short)32);f.Write(pngs[i].Length);f.Write(off);off+=pngs[i].Length;}
  foreach(var p in pngs)f.Write(p);}
 Console.WriteLine("ok");}}
