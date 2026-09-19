import urllib.request, urllib.error, http.cookiejar, json
base='http://127.0.0.1:5183'
client=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
csrf=''
def request(path,body=None,token=None):
    data=None if body is None else json.dumps(body).encode()
    r=client.open(urllib.request.Request(base+'/api/'+path,data=data,headers={'Content-Type':'application/json','X-CSRF-TOKEN':csrf if token is None else token}),timeout=30)
    return json.loads(r.read() or b'null')
def denied(path,body,status,token=None):
    try:request(path,body,token);raise AssertionError('accepted '+path)
    except urllib.error.HTTPError as e:assert e.code==status,(e.code,e.read())
denied('controller/00000000-0000-0000-0000-000000000001/delete',{},401)
session=request('session');csrf=session['csrf']
if session['setup']:request('setup',{'password':'Isolated-Deletion-Test-422!'})
request('login',{'password':'Isolated-Deletion-Test-422!'})
csrf=request('session')['csrf']
state=request('state')
request('controller',{'revision':state['revision'],'controller':{'name':'Delete test','protocol':0,'enabled':False}})
state=request('state');plc=next(c for c in state['controllers'] if c['name']=='Delete test')
for name in ['A','B']:
    request('sensor',{'revision':state['revision'],'sensor':{'controllerId':plc['id'],'name':name,'temperatureOffset':118,'temperatureFunction':3}})
    state=request('state')
sensor=next(s for s in state['sensors'] if s['controllerId']==plc['id'])
path='sensor/'+sensor['id']+'/delete'
denied(path,{'revision':state['revision']},400,'invalid-token')
denied(path,{'revision':'stale'},409)
request(path,{'revision':state['revision']});state=request('state')
assert not any(s['id']==sensor['id'] for s in state['sensors'])
assert len([s for s in state['sensors'] if s['controllerId']==plc['id']])==1
request('controller/'+plc['id']+'/delete',{'revision':state['revision']});state=request('state')
assert not any(c['id']==plc['id'] for c in state['controllers'])
assert not any(s['controllerId']==plc['id'] for s in state['sensors'])
denied('controller/'+plc['id']+'/delete',{'revision':state['revision']},404)
print('PASS packaged HTTP: authentication, antiforgery, revision, sensor deletion, controller cascade, missing ID')
