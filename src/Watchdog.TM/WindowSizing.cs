using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace Watchdog.TM;
public static class WindowSizing
{
 [StructLayout(LayoutKind.Sequential)] struct RectPixels { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size;public RectPixels Monitor,Work;public uint Flags; }
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
 public static Rect FitBounds(Rect work,double width,double height)
 {
  double inset=Math.Min(12,Math.Min(work.Width,work.Height)/10);
  double w=Math.Clamp(double.IsFinite(width)?width:1000,1,Math.Max(1,work.Width-2*inset));
  double h=Math.Clamp(double.IsFinite(height)?height:700,1,Math.Max(1,work.Height-2*inset));
  return new Rect(work.Left+(work.Width-w)/2,work.Top+(work.Height-h)/2,w,h);
 }
 public static void Attach(Window window)
 {
  window.WindowStyle=WindowStyle.SingleBorderWindow;
  window.SourceInitialized+=(_,_)=>Fit(window);
  window.Loaded+=(_,_)=>Fit(window);
 }
 public static void Fit(Window window)
 {
  if(window.WindowState!=WindowState.Normal)return;
  var work=SystemParameters.WorkArea;
  var handle=new WindowInteropHelper(window).Handle;
  if(handle!=IntPtr.Zero){var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};if(GetMonitorInfo(MonitorFromWindow(handle,2),ref info)){var transform=PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice??Matrix.Identity;var origin=transform.Transform(new Point(info.Work.Left,info.Work.Top));var end=transform.Transform(new Point(info.Work.Right,info.Work.Bottom));work=new Rect(origin,end);}}
  var bounds=FitBounds(work,window.Width,window.Height);
  window.MinWidth=Math.Min(window.MinWidth,bounds.Width);window.MinHeight=Math.Min(window.MinHeight,bounds.Height);
  window.WindowStartupLocation=WindowStartupLocation.Manual;
  window.Width=bounds.Width;window.Height=bounds.Height;window.Left=bounds.Left;window.Top=bounds.Top;
 }
}
