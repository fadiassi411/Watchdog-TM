const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('src/Watchdog.TM.Web/wwwroot/app.js','utf8');
const nodes={workspace:{hidden:false},login:{hidden:true},navigation:{hidden:false},editor:{open:false},alarmEditor:{open:false},clock:{}};
let calls=0,refreshes=0,fail=false;
const context=vm.createContext({document:{hidden:false},location:{hash:'#smtp'},Date,Promise,$:id=>nodes[id],error:()=>{},api:async()=>{calls++;if(fail)throw Error('offline');return {authenticated:true,csrf:'fresh'};},refresh:async()=>refreshes++});
vm.runInContext("let csrf='',state={};"+source.slice(source.indexOf('let polling=false'),source.indexOf('setInterval(()=>resumePolling')),context);
(async()=>{
 await vm.runInContext('resumePolling(true)',context);assert.equal(calls,1);assert.equal(refreshes,0);console.log('PASS resume keeps SMTP form intact');
 context.location.hash='#dashboard';await vm.runInContext('resumePolling(true)',context);assert.equal(refreshes,1);console.log('PASS dashboard resumes immediately');
 nodes.editor.open=true;await vm.runInContext('resumePolling(true)',context);assert.equal(refreshes,1);nodes.editor.open=false;console.log('PASS open edits preserved');
 context.document.hidden=true;await vm.runInContext('resumePolling(true)',context);assert.equal(calls,3);context.document.hidden=false;console.log('PASS hidden page pauses browser requests');
 fail=true;await vm.runInContext('resumePolling(true)',context);fail=false;await vm.runInContext('resumePolling(true)',context);assert.equal(refreshes,2);console.log('PASS offline failure recovers next attempt');
 context.api=async()=>({authenticated:false,csrf:'new'});await vm.runInContext('resumePolling(true)',context);assert.equal(nodes.login.hidden,false);assert.equal(nodes.workspace.hidden,true);console.log('PASS revoked session requires login');
})().catch(e=>{console.error(e);process.exitCode=1});
