// Requires Playwright and Chromium. Run against an isolated, fresh test server only.
// ALARM_TEST_URL=http://127.0.0.1:5184 node tests/alarm-ui.cjs
const { chromium } = require('playwright');
const assert = require('node:assert/strict');
(async () => {
  const browser = await chromium.launch({executablePath:process.env.CHROMIUM_PATH || '/usr/bin/chromium',args:['--no-sandbox']});
  try {
    const page = await browser.newPage();
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    const base=process.env.ALARM_TEST_URL || 'http://127.0.0.1:5184';
    await page.goto(base);
    const request=async(path,body,expected=200,token)=>{
      const result=await page.evaluate(async({path,body,token})=>{
        const csrf=token ?? (await fetch('/api/session').then(r=>r.json())).csrf;
        const r=await fetch('/api/'+path,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf},body:body===undefined?undefined:JSON.stringify(body)});
        return {status:r.status,text:await r.text()};
      },{path,body,token});
      assert.equal(result.status,expected,result.text);return result.text?JSON.parse(result.text):null;
    };
    await request('alarms/00000000-0000-0000-0000-000000000001/acknowledge',{episode:'unknown'},401);
    assert.equal((await request('session')).setup,true,'Use a fresh, isolated data directory');
    const password='Isolated-Alarm-Test-Only-123!';
    await page.locator('#password').fill(password);await page.locator('#loginForm button').click();
    await page.locator('#workspace').waitFor({state:'visible'});
    let state=await request('state');
    await request('controller',{revision:state.revision,controller:{name:'Alarm UI test',protocol:0,pollSeconds:1}});
    state=await request('state');const controller=state.controllers.find(c=>c.name==='Alarm UI test');
    await request('sensor',{revision:state.revision,sensor:{controllerId:controller.id,name:'Alarm sensor',temperatureOffset:1}});
    state=await request('state');const sensor=state.sensors.find(s=>s.controllerId===controller.id);const route='alarms/'+sensor.id;
    await request(route,{revision:'stale',high:-100,low:null,delaySeconds:0,hysteresis:0},409);
    await request(route,{revision:state.revision,high:-100,low:null,delaySeconds:-1,hysteresis:0},400);
    await request(route,{revision:state.revision,high:8,low:2,delaySeconds:0,hysteresis:6},400);
    await request(route+'/acknowledge',{episode:'unknown'},400,'invalid-token');
    await request(route+'/acknowledge',{episode:'unknown'},409);
    await page.reload();await page.locator(`[data-alarm="${sensor.id}"][data-focus="high"]`).click();
    await page.locator('#highLimit').fill('-100');await page.locator('#lowLimit').fill('');
    await page.locator('#alarmDelay').fill('2');await page.locator('#alarmHysteresis').fill('1');
    await page.locator('#alarmForm button[type=submit]').click();
    await page.locator('#alarmEditor').waitFor({state:'hidden'});
    state=await request('state');const saved=state.sensors.find(s=>s.id===sensor.id);assert.equal(saved.alarmDelaySeconds,2);assert.equal(saved.alarmHysteresis,1);
    const card=page.locator(`[data-sensor="${sensor.id}"]`);
    await card.locator('[data-acknowledge]').waitFor({state:'visible',timeout:20000});
    await card.locator('[data-acknowledge]').click();await card.getByText(/Acknowledged /).waitFor();
    assert.equal(await card.evaluate(el=>getComputedStyle(el).animationName),'none');
    const active=(await request('state')).readings.find(r=>r.id===sensor.id);
    assert.equal(active.alarm,'High');assert.ok(active.alarmStatus.acknowledgedAt);
    await request(route+'/acknowledge',{episode:'stale-episode'},409);
    await page.reload();await card.getByText(/Acknowledged /).waitFor();
    assert.equal(errors.length,0,errors.join('\n'));
    console.log('PASS alarm browser and HTTP: settings, validation, CSRF/authentication, stale revisions/episodes, activation, acknowledgement, stopped blinking and refresh persistence');
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
