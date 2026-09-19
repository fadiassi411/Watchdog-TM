import runpy, pathlib, urllib.request, urllib.error
helpers=runpy.run_path(str(pathlib.Path(__file__).with_name('deletion-http-tests.py')))
request=helpers['request']
state=request('state')
request('controller',{'revision':state['revision'],'controller':{'name':'144 snapshot HTTP fixture','protocol':1,'host':'127.0.0.1','enabled':False}})
state=request('state');plc=next(x for x in state['controllers'] if x['name']=='144 snapshot HTTP fixture')
assert plc['history']['capacity']==144
request('sensor',{'revision':state['revision'],'sensor':{'name':'144 sensor','controllerId':plc['id'],'temperatureOffset':118,'temperatureFunction':3}})
state=request('state');sensor=next(x for x in state['sensors'] if x['controllerId']==plc['id'])
layout=dict(plc['history']);layout.update(enabled=True,verification='SYNTHETIC HTTP TEST ONLY',headerAddress=0,headerWords=5,sequenceOffset=0,countOffset=2,positionOffset=3,statusOffset=4,bufferAddress=100,recordWords=12,recordSequenceOffset=0,recordStatusOffset=2,intervalOffset=3,timestampOffsets=[4,5,6,7,8,9],channels=[{'sensorId':sensor['id'],'offset':10,'multiplier':0.1}],segments=[{'firstRecord':0,'recordCount':72,'address':100},{'firstRecord':72,'recordCount':72,'address':10000}])
endpoint='plc-history/'+plc['id']+'/mapping'
request(endpoint,{'revision':state['revision'],'layout':layout})
state=request('state');assert next(x for x in state['controllers'] if x['id']==plc['id'])['history']['capacity']==144
layout['segments'][1]['recordCount']=73
try:request(endpoint,{'revision':state['revision'],'layout':layout});raise AssertionError('accepted 145 segmented records')
except urllib.error.HTTPError as e:assert e.code==400
layout.update(capacity=300,segments=[])
request(endpoint,{'revision':state['revision'],'layout':layout})
state=request('state');assert next(x for x in state['controllers'] if x['id']==plc['id'])['history']['capacity']==300
layout['capacity']=144
request(endpoint,{'revision':state['revision'],'layout':layout})
state=request('state');request('controller/'+plc['id']+'/delete',{'revision':state['revision']})
script=urllib.request.urlopen('http://127.0.0.1:5183/plc-history.js').read().decode()
assert '144 snapshots' in script and 'c.history.capacity/6' in script
assert 'history144r3' in urllib.request.urlopen('http://127.0.0.1:5183/').read().decode()
print('PASS packaged 144 HTTP: default, save, segmented validation, legacy 300 compatibility, 144 reconfiguration, UI/cache revision')
