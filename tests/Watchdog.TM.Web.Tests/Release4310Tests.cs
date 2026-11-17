// Regression checks for V4.3.10: the alarm-limit window must be able to save.
// V4.3.5-V4.3.9 shipped a Save button without type="submit" while the handler looked up
// button[type=submit]; the lookup returned null and the handler stopped before sending anything.
public static class Release4310Tests
{
 public static Task Run()
 {
  void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS 4.3.10 "+name);}
  var dir=new DirectoryInfo(AppContext.BaseDirectory);
  while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,"src","Watchdog.TM.Web","wwwroot","index.html")))dir=dir.Parent;
  if(dir==null)throw new Exception("Cannot locate src/Watchdog.TM.Web/wwwroot from the test output folder");
  var root=Path.Combine(dir.FullName,"src","Watchdog.TM.Web","wwwroot");
  var html=File.ReadAllText(Path.Combine(root,"index.html"));var js=File.ReadAllText(Path.Combine(root,"app.js"));
  Check(html.Contains("<button type=\"submit\">Save alarm limits"),"alarm Save button is a submit button");
  Check(!js.Contains("querySelector('button[type=submit]');button.disabled"),"alarm handler no longer dereferences an unchecked button lookup");
  Check(js.Contains("e.submitter??")&&js.Contains("if(button)button.disabled=true"),"alarm handler finds the pressed button and tolerates a missing one");
  Check(js.Contains("api('alarms/'+alarmSensorId"),"alarm handler still posts the limits");
  return Task.CompletedTask;
 }
}
