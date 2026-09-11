using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
namespace Watchdog.TM;

public sealed class PasswordDialog : Window
{
    readonly PasswordBox box = new() { Margin = new Thickness(8), Padding = new Thickness(8) }; readonly PasswordBox confirm = new() { Margin = new Thickness(8), Padding = new Thickness(8) };
    public string Value => box.Password;
    public PasswordDialog(string title, bool create = false)
    {
        Theme.Apply(this); Title = title;
        Width = 510;
        Height = create ? 300 : 225;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        if (create)
        {
            panel.Children.Add(new TextBlock { Text = "Repeat password" });
            panel.Children.Add(confirm);
        }
        var b = new Button { Content = "Continue", IsDefault = true };
        b.Click += (_, _) => { if (create && (Value.Length < 10 || Value != confirm.Password)) { MessageBox.Show("Use at least 10 characters and matching passwords."); return; } DialogResult = true; };
        panel.Children.Add(b);
        Content = panel;
        Loaded += (_, _) => box.Focus();
    }
}

public sealed class Editor : Window
{
 readonly Dictionary<PropertyInfo,FrameworkElement> fields=[];
 readonly Dictionary<string,FrameworkElement> containers=[];
 readonly object target; readonly StackPanel sections=new(); readonly TextBlock error=new(){TextWrapping=TextWrapping.Wrap,Foreground=System.Windows.Media.Brushes.Firebrick,Margin=new Thickness(4)};
 public sealed record Choice(object Value,string Label);
 public Editor(object target,string title)
 {
  this.target=target;Theme.Apply(this);Title=title;Width=840;Height=850;MinWidth=650;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  var dock=new DockPanel{Margin=new Thickness(24)};
  var top=new StackPanel();top.Children.Add(new TextBlock{Text="WATCHDOG TM  /  SETUP",Foreground=Theme.Teal,FontSize=11,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,8)});top.Children.Add(Theme.Heading(target is Sensor?"Temperature sensor":target is Controller?"Controller setup":"Application settings"));top.Children.Add(Theme.Note(target is Sensor?"Enter the temperature register and value format. Live monitoring reads only this value.":target is Controller?"Choose the connection used by this controller. Only the relevant connection settings are shown.":"Manage local monitoring, backups and outgoing alarm email."));DockPanel.SetDock(top,Dock.Top);dock.Children.Add(top);
  var footer=new StackPanel();footer.Children.Add(error);var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var cancel=new Button{Content="Cancel",IsCancel=true,Background=Theme.Muted};var save=new Button{Content=target is Sensor?"Save sensor":target is Controller?"Save controller":"Save settings",IsDefault=true};save.Click+=(_,_)=>Save();buttons.Children.Add(cancel);buttons.Children.Add(save);footer.Children.Add(buttons);DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
  if(target is Controller) ControllerForm();else if(target is Sensor s) SensorForm(s);else SettingsForm();
  dock.Children.Add(new ScrollViewer{Content=sections,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=dock;
 }
 void Section(string title,string note,string[] properties,bool collapsed=false)
 {
  var body=new StackPanel();body.Children.Add(Theme.Heading(title,17));if(note.Length>0)body.Children.Add(Theme.Note(note));var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());int index=0;
  foreach(var name in properties){var prop=target.GetType().GetProperty(name)!;var cell=new StackPanel{Margin=new Thickness(index%2==0?0:12,0,index%2==0?12:0,12)};cell.Children.Add(new TextBlock{Text=Label(name),FontWeight=FontWeights.SemiBold,FontSize=12});var type=Nullable.GetUnderlyingType(prop.PropertyType)??prop.PropertyType;FrameworkElement input;
   if(type==typeof(bool))input=new CheckBox{IsChecked=(bool)prop.GetValue(target)!,Content=name=="Enabled"?"Enabled":name=="Retired"?"Retired — monitoring stopped":"Enable"};
   else if(type.IsEnum){var items=Enum.GetValues(type).Cast<object>().Select(v=>new Choice(v,EnumLabel(v))).ToList();input=new ComboBox{ItemsSource=items,DisplayMemberPath="Label",SelectedValuePath="Value",SelectedValue=prop.GetValue(target)};}
   else input=new TextBox{Text=Convert.ToString(prop.GetValue(target),CultureInfo.InvariantCulture)??""};
   input.Tag=name;cell.Children.Add(input);fields[prop]=input;containers[name]=cell;
   if(name=="TemperatureOffset"&&input is TextBox address){var preview=Theme.Note("");void Update()=>preview.Text=ushort.TryParse(address.Text,out var value)?$"Protocol address sent: {value} (zero-based)":"Leave blank until the PLC address is confirmed.";address.TextChanged+=(_,_)=>Update();Update();cell.Children.Add(preview);}
   if(index%2==0)grid.RowDefinitions.Add(new(){Height=GridLength.Auto});Grid.SetRow(cell,index/2);Grid.SetColumn(cell,index%2);grid.Children.Add(cell);index++;
  }
  body.Children.Add(grid);if(collapsed){var expander=new Expander{Header=title,Content=body,IsExpanded=false};sections.Children.Add(Theme.Panel(expander));}else sections.Children.Add(Theme.Panel(body));
 }
 void ControllerForm()
 {
  Section("1. Controller identity","",["Name","Enabled","Protocol","UnitId"]);
  Section("2. Connection","Match these values to the controller's configured communication settings.",["ComPort","BaudRate","Parity","DataBits","StopBits","Host","TcpPort"]);
  Section("Advanced · polling and retries","Live polling is separate from the temperature history interval.",["PollSeconds","TimeoutMs","Retries"],true);
  var selector=(ComboBox)fields.First(x=>x.Key.Name=="Protocol").Value;void Update(){var selected=selector.SelectedValue is Protocol p?p:Protocol.TCP;foreach(var name in new[]{"ComPort","BaudRate","Parity","DataBits","StopBits"})containers[name].Visibility=selected==Protocol.RTU?Visibility.Visible:Visibility.Collapsed;foreach(var name in new[]{"Host","TcpPort"})containers[name].Visibility=selected==Protocol.TCP?Visibility.Visible:Visibility.Collapsed;containers["UnitId"].Visibility=selected==Protocol.Simulation?Visibility.Collapsed:Visibility.Visible;}selector.SelectionChanged+=(_,_)=>Update();Update();
 }
 void SensorForm(Sensor sensor)
 {
  if(sensor.Retired){var message=new StackPanel();message.Children.Add(Theme.Heading("This sensor is retired",17));message.Children.Add(Theme.Note("It is not monitored. Use Reactivate on the controller screen when you want to return it to service."));sections.Children.Add(Theme.Panel(message));}
  Section("1. Refrigerator","",["Name","Location","Enabled"]);
  Section("2. Temperature reading","Enter the verified zero-based Modbus address. No Delta register conversion is applied.",["TemperatureOffset","TemperatureFunction","Format","Multiplier"]);
  Section("3. Recording","Choose how often to save readings. Live updates use the controller polling interval.",["SampleSeconds","Decimals"]);
  Section("Advanced · temperature format","Change only when required by the PLC register map.",["Order","Offset"],true);
 }
 void SettingsForm()
 {
  Section("Monitoring","Use the operating-mode controls on the main Settings screen to switch between demonstration and live monitoring.",["Site","AudibleAlarm"]);
  Section("Local backups","Choose a dedicated backup folder.",["DailyBackup","BackupFolder","RetentionDays"]);
  Section("SMTP email","Set the password separately using Set SMTP password.",["SmtpHost","SmtpPort","SslOnConnect","SmtpUsername","SenderEmail","SenderName"],true);
 }
 void Save()
 {
  try{foreach(var(p,input)in fields){var type=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;object? value=input switch{CheckBox check=>check.IsChecked==true,ComboBox combo=>combo.SelectedValue,TextBox text when text.Text.Length==0&&Nullable.GetUnderlyingType(p.PropertyType)!=null=>null,TextBox text=>Convert.ChangeType(text.Text,type,CultureInfo.InvariantCulture),_=>null};p.SetValue(target,value);}DialogResult=true;}catch{error.Text="Check the entered numbers. Use a decimal point and leave unknown addresses blank.";}
 }
 static string EnumLabel(object value)=>value switch{Protocol.RTU=>"Modbus RTU · RS-485",Protocol.TCP=>"Modbus TCP/IP · Ethernet",Protocol.Simulation=>"Demonstration controller",RegisterFunction.Holding03=>"Holding register · FC03",RegisterFunction.Input04=>"Input register · FC04",BitFunction.Coil01=>"Coil · FC01",BitFunction.Discrete02=>"Discrete input · FC02",BitFunction.Holding03=>"Holding register bit · FC03",BitFunction.Input04=>"Input register bit · FC04",ValueFormat.Int16=>"Signed 16-bit",ValueFormat.UInt16=>"Unsigned 16-bit",ValueFormat.Int32=>"Signed 32-bit",ValueFormat.UInt32=>"Unsigned 32-bit",ValueFormat.Float32=>"32-bit floating point",_=>value.ToString()!};
 static string Label(string name)=>name switch{"Name"=>"Name","Location"=>"Location / description","Protocol"=>"Connection type","UnitId"=>"PLC slave / unit address","ComPort"=>"Windows COM port","Host"=>"IP address or hostname","TcpPort"=>"TCP port","TemperatureOffset"=>"Temperature register address","TemperatureFunction"=>"Read function","Format"=>"Value format","Multiplier"=>"Temperature multiplier","Offset"=>"Temperature correction (°C)","Order"=>"Byte / word order","SampleSeconds"=>"Record every (seconds)","Decimals"=>"Displayed decimal places","PollSeconds"=>"Read every (seconds)","TimeoutMs"=>"Response timeout (ms)","HighAlarmOffset"=>"PLC high-alarm address","HighAlarmRegisterBit"=>"Bit number (register reads only)","HighAlarmFunction"=>"Alarm read function","HighLimitOffset"=>"Writable high-limit register","LimitMultiplier"=>"Limit multiplier","MinimumLimit"=>"Minimum permitted limit (°C)","MaximumLimit"=>"Maximum permitted limit (°C)","SslOnConnect"=>"Use SSL/TLS (off = STARTTLS)",_=>System.Text.RegularExpressions.Regex.Replace(name,"([a-z])([A-Z])","$1 $2")};
}

