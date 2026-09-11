from pathlib import Path
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph, Table, TableStyle
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.colors import HexColor, white
from reportlab.lib.enums import TA_LEFT
import sys
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'src/Watchdog.TM.Web/wwwroot/help/Watchdog-TM-Help.pdf'
OUT.parent.mkdir(parents=True,exist_ok=True)
W,H=595.276,841.89
NAVY=HexColor('#102b3b'); TEAL=HexColor('#078c86'); INK=HexColor('#193242'); MUTED=HexColor('#637887'); PALE=HexColor('#eef6f6')
c=canvas.Canvas(str(OUT),pagesize=(W,H));c.setTitle('Watchdog TM V4.2.1 | Customer Help Guide');c.setAuthor('MicroBrain');c.setSubject('Installation, temperature monitoring, software alarms, trends and monthly Excel exports')
styles={
 'body':ParagraphStyle('body',fontName='Helvetica',fontSize=10.4,leading=16,textColor=INK,spaceAfter=10),
 'small':ParagraphStyle('small',fontName='Helvetica',fontSize=9,leading=13,textColor=MUTED),
 'head':ParagraphStyle('head',fontName='Helvetica-Bold',fontSize=14,leading=20,textColor=INK),
 'table':ParagraphStyle('table',fontName='Helvetica',fontSize=9.5,leading=14,textColor=INK),
}
page=0;y=0

def text(s,style='body',x=46,width=W-92):
 global y
 p=Paragraph(s,styles[style]);_,h=p.wrap(width,1000)
 if y-h<62:raise RuntimeError(f'Page {page} overflow: {s[:60]}')
 p.drawOn(c,x,y-h);y-=h+10

def heading(s):
 global y
 y-=8;text(s,'head')

def note(s):
 global y
 p=Paragraph(s,styles['body']);_,h=p.wrap(W-124,1000)
 if y-h-25<62:raise RuntimeError(f'Page {page}: note overflow')
 c.setFillColor(PALE);c.roundRect(46,y-h-23,W-92,h+20,7,fill=1,stroke=0)
 p.drawOn(c,62,y-h-11);y-=h+38

def table(rows,widths):
 global y
 data=[[Paragraph(str(v),styles['table']) for v in row] for row in rows]
 t=Table(data,colWidths=widths,hAlign='LEFT');t.setStyle(TableStyle([('BACKGROUND',(0,0),(-1,0),PALE),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),10),('RIGHTPADDING',(0,0),(-1,-1),10),('TOPPADDING',(0,0),(-1,-1),9),('BOTTOMPADDING',(0,0),(-1,-1),9),('LINEBELOW',(0,0),(-1,-1),.4,HexColor('#dde6ea'))]))
 _,h=t.wrap(W-92,1000)
 if y-h<62:raise RuntimeError(f'Page {page}: table overflow')
 t.drawOn(c,46,y-h);y-=h+17

def new(key,title,sub):
 global page,y
 if page:c.showPage()
 page+=1;c.bookmarkPage(key);c.addOutlineEntry(title,key,0)
 c.setFillColor(NAVY);c.rect(0,H-14,W,14,fill=1,stroke=0)
 c.setFillColor(TEAL);c.setFont('Helvetica-Bold',9);c.drawString(46,H-48,'WATCHDOG TM  /  CUSTOMER HELP')
 c.setFillColor(INK);c.setFont('Helvetica-Bold',25);c.drawString(46,H-87,title)
 y=H-108;text(sub,'small');y-=12
 c.setStrokeColor(HexColor('#dde6ea'));c.line(46,45,W-46,45)
 c.setFillColor(MUTED);c.setFont('Helvetica',8);c.drawString(46,30,'MicroBrain  |  Watchdog TM V4.2.1  |  11 September 2026');c.drawRightString(W-46,30,f'{page:02d}')
 c.linkRect('', 'contents',(46,22,190,40),relative=0,thickness=0)

new('contents','Watch every degree.','WATCHDOG TEMPERATURE MONITORING  |  V4.2.1  |  WINDOWS SERVER')
heading('Customer Help Guide')
text('Set up your controller, monitor readings, configure local alarms and retrieve temperature history from any browser on your local network.')
note('<b>Always-on monitoring</b><br/>Watchdog runs as a Windows Service. Closing the browser does not stop collection. Keep the server computer powered, awake and connected to the controller.')
heading('Find what you need')
for label,key,num in [('Install and connect','install','02'),('Controller communication','controller','03'),('Sensor and register setup','sensor','04'),('Dashboard and software alarms','alarms','05'),('Explore temperature trends','trends','06'),('Monthly Excel reports','excel','07'),('Backups and daily operation','backup','08'),('Troubleshooting and About','support','09')]:
 text(f'<link href="#{key}" color="#078c86">{num} &nbsp; {label}</link>')
y-=10
text('This guide describes the V4.2.1 web server. Older desktop screens may differ. Example register addresses and alarm limits are illustrations, not a controller commissioning map.','small')

new('install','01 / Install and connect','Audience: the person installing or maintaining the server.')
heading('Install on the server computer')
text('1. If the old Watchdog TM desktop app is running, save your edits and close it.<br/>2. Run <b>Watchdog-TM-Server-4.2.1-Setup.exe</b> with administrator approval.<br/>3. Wait for installation and service startup to finish.<br/>4. Open the <b>Watchdog TM Server</b> shortcut, or browse to <b>http://localhost:5081</b>.')
heading('Sign in')
text('On a new installation, create an administrator password from the server computer. Use at least 10 characters. An existing installation keeps its current administrator password, license, configuration and history.')
heading('Connect from another computer or phone')
text('Connect the device to the same local network, then open <b>http://SERVER-IP:5081</b>, replacing SERVER-IP with the server computer\'s IPv4 address. Sign in with the same administrator password. The controller adapter stays attached to the server, not the browsing device.')
note('<b>For reliable access</b><br/>Ask your network administrator to reserve the server IP address. The installer permits local-subnet access on TCP port 5081. Routed networks or VLANs may require a site-specific firewall rule.')
heading('Activate live monitoring once')
text('Configure the controller and sensor, import a signed TM license under <b>Settings &gt; Monitoring and license</b>, then select <b>Switch to live monitoring</b> if the app is in demonstration mode. Subsequent edits apply automatically. Live mode resumes when the service restarts.')
text('This release is for a trusted local network. Internet access, VPN and HTTPS gateway setup are separate network tasks.','small')

new('controller','02 / Controller communication','Main menu: Settings > Controllers and sensors')
text('Select <b>Add controller</b> or edit an existing controller. Enable only the controllers you intend to monitor. Settings must match the physical controller and wiring.')
table([['Connection','Values to check'],['Modbus RTU','COM port, unit ID, baud rate, parity, data bits and stop bits. Check the adapter COM number in Windows Device Manager.'],['Modbus TCP','Controller IP address (Host), TCP port and unit ID. TCP port 502 is common; use the actual controller setting.'],['Polling and timeout','Poll seconds controls how often the controller is read. Timeout and retries determine how long Watchdog waits before reporting a failed request.']],[130,W-222])
heading('Test the connection')
text('1. Save the controller and add a sensor with a verified temperature register.<br/>2. Select <b>Test connection</b> for the controller.<br/>3. Check the returned register, value and time.<br/>4. In live mode, return to the dashboard and confirm the last reading continues to advance.')
note('<b>A successful test is one read</b><br/>It confirms a response at that moment. Continuous live monitoring is confirmed by fresh dashboard readings over time.')
heading('When there is no response')
text('Check power, cable/adapter type, the COM port or IP address, unit ID, and serial settings. Ensure another application is not using the same COM port. Verify the RS-485 connection against the equipment documentation. A test failure alone does not identify which setting or wire is wrong.')

new('sensor','03 / Sensor and register setup','Each sensor has one configured temperature value.')
text('Open <b>Settings &gt; Controllers and sensors</b> and add or edit a sensor under its controller. Choose a meaningful name and location, then enter the verified register mapping.')
table([['Field','Meaning'],['Temperature register','Zero-based protocol offset. Example: enter 4196 only if the controller map specifies offset 4196. Watchdog does not automatically convert PLC register names.'],['Read function','FC03 holding registers or FC04 input registers, according to the controller map.'],['Value format / order','Signed/unsigned 16-bit or 32-bit value, or Float32. A 32-bit value uses two consecutive registers. Match byte/word order.'],['Multiplier / correction','Displayed value = decoded register value x multiplier + correction. Example: raw 77 with multiplier 0.1 gives 7.7.'],['Enabled / Retired','Disabled or retired sensors stop monitoring. Retirement affects license capacity; disabled sensors still occupy their slot.'],['Record every','Interval between saved history entries. This is separate from controller polling.']],[145,W-237])
note('<b>Read-only operation</b><br/>Watchdog reads the configured FC03/FC04 value. It does not write alarm thresholds or other values to the PLC. No PLC alarm, fault or limit register mapping is required for the web dashboard alarms.')
text('A kWh register may prove communication, but the displayed °C label does not turn an energy value into a temperature measurement.','small')

new('alarms','04 / Dashboard and alarms','Main > Dashboard')
text('Each card shows the sensor name, location, controller, reading quality, value and last reading time. Select <b>Trend</b> beside the value to open that sensor\'s history.')
heading('Set local high and low limits')
text('1. Select <b>High Alarm</b> or <b>Low Alarm</b> on the sensor card.<br/>2. Enter the required limit values. If both are enabled, Low must be below High.<br/>3. Leave a field blank to turn that limit off.<br/>4. Select <b>Save alarm limits</b>. Watchdog applies the limits internally.')
table([['Condition','Card behavior'],['Reading above High Alarm','Red and blinking.'],['Reading below Low Alarm','Yellow and blinking.'],['Reading within limits','Normal reading indication. A reading exactly at a limit does not trigger that alarm.'],['Offline, stale, disabled or fault','Current temperature alarm evaluation is unavailable. A last known value must not be treated as a fresh measurement.']],[205,W-297])
note('<b>Example only</b><br/>With Low = 2 °C and High = 8 °C, 1.9 °C triggers Low and 8.1 °C triggers High. At 2 °C or 8 °C, no temperature alarm is raised. Select limits appropriate to your equipment and operating requirements.')
heading('What the alarm does')
text('The browser card displays the current software alarm condition. This web release does not enable automatic alarm email delivery. Reduced-motion browser preferences may show a steady alarm color instead of blinking.')

new('trends','05 / Explore temperature trends','Main > Trends and export, or Trend on a sensor card')
heading('Choose the view')
text('Select the <b>Sensor</b>, then choose a <b>Period</b>: last hour, 6 hours, 24 hours, today, yesterday, 7 days, 30 days, 90 days, last year, or a custom period. For custom dates, enter <b>From</b> and <b>To</b> and select <b>Apply period</b>. A chart request can cover up to one year.')
table([['Control','Use'],['Chart interval','Choose the duration represented by each point. Automatic adjusts density. Large periods may enlarge a requested short interval to keep the chart readable.'],['Previous / next arrows','Move backward or forward by the current period length.'],['Zoom in / Last 24 hours','Inspect the central half of the current period, or return to the standard 24-hour view.'],['Follow latest (30 sec)','Refresh a rolling view every 30 seconds while the page is visible.'],['Hover / keyboard arrows','Inspect the average, minimum, maximum and quality detail for a chart interval. Focus the chart and use left/right arrow keys.']],[158,W-250])
heading('Read the chart correctly')
text('The line shows interval averages; the shaded range preserves minimum and maximum values. Dashed lines show the <b>current</b> alarm limits, not historical limit changes. Gaps and interrupted intervals are not connected. Demonstration readings are identified in the chart note.')
heading('Change the saved-history interval')
text('Expand <b>Recording interval</b>. Enter a quantity, choose seconds, minutes or hours, and select <b>Save interval</b>. Allowed values are 1 second to 24 hours. The change affects future entries; live monitoring continues automatically.')
text('Chart interval changes the display only. Recording interval changes how often history is saved. Neither increases the controller polling rate.','small')

new('excel','06 / Monthly Excel reports','Download historical readings for the required month and year.')
heading('Export a month')
text('1. Open <b>Main &gt; Trends and export</b>.<br/>2. Select the required sensor.<br/>3. Choose <b>Custom period</b>.<br/>4. Set From to the first day of the required month/year at 00:00.<br/>5. Set To to the first day of the next month at 00:00.<br/>6. Select <b>Export Excel</b> and save the downloaded file.')
note('<b>Example: January 2027</b><br/>From: 1 January 2027, 00:00<br/>To: 1 February 2027, 00:00<br/>Suggested filename: Fridge-01_2027-01.xlsx')
text('The selected end time is inclusive. If a row exists exactly at the following month\'s midnight, exclude that boundary row from a month-only report. Exports are limited to 31 elapsed days; if a daylight-saving change makes your range longer, split it into two exports.')
heading('What is included')
text('Excel contains original stored entries, not the chart\'s averaged points: sensor, controller, date, time, temperature, quality/status, server timezone, UTC timestamp and UTC offset. Invalid readings are blank instead of zero. Exports use the server\'s timezone; date selection in the browser uses the viewing device\'s local time.')
heading('Organize yearly records')
text('Save files in a folder for each year, with sensor and month in the filename. Readings remain in the Watchdog database without automatic sample deletion, subject to available disk space. You can return later to export an older month that has been recorded.')
text('Excel export downloads data to your device. Importing Excel files back into Watchdog is not supported.','small')

new('backup','07 / Backups and daily operation','Settings > Backup and restore')
heading('Download a backup')
text('Select <b>Download database backup</b> and save the .db file to a protected location. It includes configuration, license, administrator password hash and historical readings. Keep a copy outside the server computer and restrict access to authorized staff.')
heading('Restore a backup')
text('Choose the .db file under <b>Restore database</b> and select <b>Restore selected backup</b>. Restoring replaces current configuration and history; Watchdog first creates a recovery backup. Your current password and license remain in use. After restore, review the controller/sensor settings and switch from demonstration mode back to live monitoring.')
note('<b>Backup is different from Excel</b><br/>An Excel export is a report. A database backup is the recovery file used by Watchdog. Keep both when your customer needs monthly records and recovery protection.')
heading('Daily operator check')
text('Confirm that the header says LIVE, the expected sensors are enabled, and the last reading time advances. Investigate offline/stale cards or alarm colors. Keep the server clock/timezone correct and check that sufficient disk space remains.')
heading('After edits or restart')
text('Saving a controller, sensor, recording interval or alarm limit applies automatically while live mode is enabled. The Windows Service starts automatically after Windows startup. Closing the browser, signing out of the web app or locking Windows does not stop the service. Sleep, shutdown, power loss or adapter disconnection interrupts collection.')
heading('Password and license')
text('Use <b>Settings &gt; Monitoring and license</b> to change the administrator password or import a signed TM license. Do not distribute database backups as installation packages. For licensing assistance, provide the installation ID shown in the application to your supplier.')

new('support','08 / Troubleshooting and About','Use the symptom to narrow the next check.')
table([['Symptom','Next check'],['Website does not open','Check server power and the WatchdogTM service. On the server try http://localhost:5081. On another device check the server IP, network and firewall.'],['Demonstration banner','Review the real controller/sensor configuration and license, then switch to live monitoring once.'],['No controller reading','Check COM/IP, unit ID, serial settings, wiring and register map. Use Test connection. Close other programs using the COM port.'],['Value is unexpected','Verify register offset, FC03/FC04, signed/unsigned format, word order and scaling. Do not change the PLC program just to match an assumed map.'],['Trend is empty','Check the chosen sensor, period and server/device clocks. Only recorded history can be displayed. Invalid readings appear as gaps.'],['Excel export is refused','Select a valid From/To range of up to 31 elapsed days. Split a longer period.'],['Old page after an update','Refresh the browser with Ctrl+F5 to reload its files.']],[150,W-242])
heading('About Watchdog TM')
text('<b>Product:</b> Watchdog Temperature Monitoring<br/><b>Version:</b> V4.2.1 Web<br/><b>Publisher:</b> MicroBrain by Fadi Assi<br/><b>Platform:</b> .NET 10, Windows x64, Windows Service<br/><b>Protocol:</b> Read-only Modbus RTU/TCP, FC03/FC04<br/><b>Storage:</b> Local SQLite database')
text('Open <b>Main &gt; About</b> for this installation\'s license status and installation ID. Open <b>Help</b> to view or download this guide. When reporting an issue, include the version, affected sensor, exact message and time; exclude passwords and private backups.','small')
c.save();print(OUT);print(f'{page} pages')
