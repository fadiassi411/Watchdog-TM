import urllib.request,urllib.error,http.cookiejar,json,zipfile,io
base='http://127.0.0.1:5182'
jar=http.cookiejar.CookieJar();client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar));csrf=''
def req(path,body=None):
 data=None if body is None else json.dumps(body).encode()
 r=client.open(urllib.request.Request(base+path,data=data,headers={'Content-Type':'application/json','X-CSRF-TOKEN':csrf}),timeout=30)
 return r.read()
def obj(path,body=None):return json.loads(req(path,body) or 'null')
def check(ok,name):assert ok,name;print('PASS HTTP '+name)
try:req('/api/plc-history');raise AssertionError('anonymous accepted')
except urllib.error.HTTPError as e:check(e.code==401,'history requires authentication')
s=obj('/api/session');csrf=s['csrf']
if s['setup']:obj('/api/setup',{'password':'Isolated-History-Test-422!'})
obj('/api/login',{'password':'Isolated-History-Test-422!'})
s=obj('/api/session');csrf=s['csrf']
check(s['authenticated'],'session login')
h=obj('/api/plc-history');check(isinstance(h['controllers'],list),'independent controller layouts endpoint')
check(obj('/api/stored-history')==[],'empty combined history is valid')
check(b'Timestamp UTC' in req('/api/stored-history/export?format=csv'),'all-time CSV export')
x=req('/api/stored-history/export?format=xlsx');check('xl/workbook.xml' in zipfile.ZipFile(io.BytesIO(x)).namelist(),'all-time Excel export')
page=req('/').decode();check('plc-history.js' in page and 'V4.2.2' in page,'published version and history navigation')
try:req('/api/plc-history/00000000-0000-0000-0000-000000000001/download',{});raise AssertionError('missing PLC accepted')
except urllib.error.HTTPError as e:check(e.code==400,'unconfigured PLC downloads blocked')
