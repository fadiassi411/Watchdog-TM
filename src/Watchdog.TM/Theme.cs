using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Watchdog.TM;
public static class Theme
{
 public static readonly Brush Navy = Brush("#0C2637"), Teal = Brush("#0C8C84"), Muted = Brush("#6A7884"), Line = Brush("#DCE4E7"), Background = Brush("#F3F6F7");
 public static Brush Brush(string hex)=>(Brush)new BrushConverter().ConvertFromString(hex)!;
 public static void Apply(Window window){WindowSizing.Attach(window);window.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/Watchdog TM;component/Theme.xaml",UriKind.Relative)});window.FontFamily=new FontFamily("Segoe UI");window.FontSize=14;window.Foreground=Brush("#14212B");window.Background=Background;}
 public static Border Panel(UIElement child)=>new(){Child=child,Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Padding=new Thickness(20),Margin=new Thickness(0,0,0,16)};
 public static TextBlock Heading(string text,int size=23)=>new(){Text=text,FontSize=size,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,8)};
 public static TextBlock Note(string text)=>new(){Text=text,Foreground=Muted,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),FontSize=12};
}

