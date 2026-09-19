const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/Watchdog.TM.Web/wwwroot/app.js','utf8');
const content={innerHTML:''};let calls=[],accepted=false,prompt='';
const context=vm.createContext({state:{revision:'r1',controllers:[{id:'c1',name:'PLC <one>',protocol:1,enabled:true}],sensors:[{id:'s1',controllerId:'c1',name:'Sensor one',temperatureOffset:118}],states:{},simulation:false},$:()=>content,title:()=>'',esc:v=>String(v??'').replaceAll('<','&lt;'),date:()=>'',confirm:s=>{prompt=s;return accepted},api:async(...args)=>{calls.push(args);return {message:'Deleted'}},refresh:async()=>{},error:()=>{}});
vm.runInContext(source.slice(source.indexOf('function controllers()'),source.indexOf('const controllerFields')),context);
vm.runInContext(source.slice(source.indexOf('async function deleteEntry')),context);
(async()=>{
 vm.runInContext('controllers()',context);assert.match(content.innerHTML,/data-delete-controller="c1"/);assert.match(content.innerHTML,/data-delete-sensor="s1"/);assert.match(content.innerHTML,/PLC &lt;one>/);
 await vm.runInContext("deleteEntry(true,'c1')",context);assert.equal(calls.length,0);assert.match(prompt,/1 sensor\/register/);
 accepted=true;await vm.runInContext("deleteEntry(false,'s1')",context);assert.equal(calls[0][0],'sensor/s1/delete');assert.equal(calls[0][1].revision,'r1');
 await vm.runInContext("deleteEntry(true,'c1')",context);assert.equal(calls[1][0],'controller/c1/delete');
 console.log('PASS deletion UI: buttons, escaping, cancelled confirmation, sensor/controller targets and revision');
})().catch(e=>{console.error(e);process.exitCode=1});
