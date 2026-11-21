'use strict';
// V4.5.0: Administrator page for user levels (permission checkboxes) and user accounts.
let usersData=null,userDialogSave=null;

async function usersPage(){
 usersData=await api('users');if(location.hash!=='#users')return;
 const d=usersData,label=id=>d.catalog.find(p=>p.id===id)?.label??id,levelName=id=>d.levels.find(l=>l.id===id)?.name??'—';
 const sensorText=l=>l.allSensors?'All sensors':l.sensors.map(id=>d.sensors.find(s=>s.id===id)?.name).filter(Boolean).join(', ')||'No sensors';
 $('content').innerHTML=title('Users and levels','Create user levels, tick what each level can monitor and control, then give each person their own user name and password. The Administrator always has full access.')+
 `<section class="panel"><h2>User levels</h2><p class="muted">Every level can see the dashboard for its sensors. Tick extra permissions for each level.</p><div class="actions"><button data-level-add>Add user level</button></div>${d.levels.length?`<div class="scroll"><table><thead><tr><th>Level</th><th>Allowed</th><th>Sensors</th><th>Users</th><th>Actions</th></tr></thead><tbody>${d.levels.map(l=>`<tr><td><strong>${esc(l.name)}</strong></td><td><div class="permission-tags">${l.permissions.length?l.permissions.map(p=>`<span>${esc(label(p))}</span>`).join(''):'<span>View dashboard only</span>'}</div></td><td>${esc(sensorText(l))}</td><td>${d.users.filter(u=>u.levelId===l.id).length}</td><td><button class="secondary" data-level-edit="${l.id}">Edit</button> <button class="danger" data-level-delete="${l.id}">Delete</button></td></tr>`).join('')}</tbody></table></div>`:'<p>No user levels yet. Add one, for example "Operator" or "Viewer".</p>'}</section>`+
 `<section class="panel"><h2>Users</h2><div class="actions"><button data-user-add ${d.levels.length?'':'disabled title="Add a user level first"'}>Add user</button></div>${d.users.length?`<div class="scroll"><table><thead><tr><th>User name</th><th>Level</th><th>Status</th><th>Actions</th></tr></thead><tbody>${d.users.map(u=>`<tr><td>${esc(u.name)}</td><td>${esc(levelName(u.levelId))}</td><td>${u.enabled?'Enabled':'Disabled'}</td><td><button class="secondary" data-user-edit="${u.id}">Edit</button> <button class="danger" data-user-delete="${u.id}">Delete</button></td></tr>`).join('')}</tbody></table></div>`:'<p>No users yet. Until you add users, only the Administrator can sign in.</p>'}<p class="muted">Users sign in with their user name and password. Leaving the user name blank signs in as the Administrator. Changing a user's password, level or status applies immediately.</p></section>`;
}

function openUserDialog(heading,html,save){$('userDialogTitle').textContent=heading;$('userDialogFields').innerHTML=html;$('userDialogError').textContent='';userDialogSave=save;$('userDialog').showModal();}
$('userDialogCancel').onclick=()=>$('userDialog').close();
$('userForm').onsubmit=async e=>{e.preventDefault();const button=e.submitter??e.target.querySelector('button[type=submit]');if(button)button.disabled=true;try{await userDialogSave();$('userDialog').close();await usersPage();}catch(err){$('userDialogError').textContent=err.message;}finally{if(button)button.disabled=false;}};

function levelDialog(level){
 const d=usersData,l=level??{id:'00000000-0000-0000-0000-000000000000',name:'',permissions:[],allSensors:true,sensors:[]};
 const groups=[...new Set(d.catalog.map(p=>p.group))];
 const controllers=[...new Set(d.sensors.map(s=>s.controller??''))];
 openUserDialog(level?'Edit user level':'Add user level',
  `<label>Level name<input id="levelName" required maxlength="64" value="${esc(l.name)}" placeholder="Operator"></label>`+
  groups.map(g=>`<fieldset class="permission-group"><legend>${g==='Monitor'?'What this level can monitor':'What this level can control'}</legend>${g==='Monitor'?'<label class="permission-option"><input type="checkbox" checked disabled><span>View the dashboard<small>Live temperatures and alarm status (always allowed).</small></span></label>':''}${d.catalog.filter(p=>p.group===g).map(p=>`<label class="permission-option"><input type="checkbox" data-permission="${p.id}" ${l.permissions.includes(p.id)?'checked':''}><span>${esc(p.label)}<small>${esc(p.description)}</small></span></label>`).join('')}</fieldset>`).join('')+
  `<fieldset class="permission-group"><legend>Which sensors this level can see</legend><label class="permission-option"><input type="checkbox" id="levelAllSensors" ${l.allSensors?'checked':''}><span>All sensors<small>Includes sensors added later.</small></span></label><div id="levelSensors" ${l.allSensors?'hidden':''}>${controllers.map(c=>`<p class="muted">${esc(c)}</p><div class="sensor-choices">${d.sensors.filter(s=>(s.controller??'')===c).map(s=>`<label><input type="checkbox" data-level-sensor="${s.id}" ${l.sensors.includes(s.id)?'checked':''}>${esc(s.name)}</label>`).join('')}</div>`).join('')||'<p>No sensors configured.</p>'}</div></fieldset><p class="muted">Users, levels, license, backup and restore are always Administrator-only.</p>`,
  ()=>api('users/level',{revision:usersData.revision,id:l.id,name:$('levelName').value,
   permissions:[...$('userDialogFields').querySelectorAll('[data-permission]:checked')].map(x=>x.dataset.permission),
   allSensors:$('levelAllSensors').checked,sensors:[...$('userDialogFields').querySelectorAll('[data-level-sensor]:checked')].map(x=>x.dataset.levelSensor)}));
 $('levelAllSensors').onchange=()=>{$('levelSensors').hidden=$('levelAllSensors').checked;};
}

function userDialog(user){
 const d=usersData,u=user??{id:'00000000-0000-0000-0000-000000000000',name:'',levelId:d.levels[0]?.id,enabled:true};
 openUserDialog(user?'Edit user':'Add user',
  `<div class="form-grid"><label>User name<input id="accountName" required maxlength="64" autocomplete="off" value="${esc(u.name)}"></label><label>User level<select id="accountLevel">${d.levels.map(l=>`<option value="${l.id}" ${l.id===u.levelId?'selected':''}>${esc(l.name)}</option>`).join('')}</select></label></div>`+
  `<label>${user?'New password (leave blank to keep the current one)':'Password (at least 10 characters)'}<input id="accountPassword" type="password" autocomplete="new-password" maxlength="256" ${user?'':'required minlength="10"'}></label>`+
  `<label class="permission-option"><input type="checkbox" id="accountEnabled" ${u.enabled?'checked':''}><span>Enabled<small>A disabled user cannot sign in; an open session ends immediately.</small></span></label>`,
  ()=>api('users/user',{revision:usersData.revision,id:u.id,name:$('accountName').value,levelId:$('accountLevel').value,enabled:$('accountEnabled').checked,password:$('accountPassword').value||null}));
}

$('content').addEventListener('click',async e=>{
 if(location.hash!=='#users')return;const b=e.target.closest('button');if(!b||!usersData)return;
 try{
  if(b.hasAttribute('data-level-add'))levelDialog(null);
  else if(b.dataset.levelEdit)levelDialog(usersData.levels.find(l=>l.id===b.dataset.levelEdit));
  else if(b.hasAttribute('data-user-add'))userDialog(null);
  else if(b.dataset.userEdit)userDialog(usersData.users.find(u=>u.id===b.dataset.userEdit));
  else if(b.dataset.levelDelete){const l=usersData.levels.find(x=>x.id===b.dataset.levelDelete);if(confirm(`Delete user level "${l.name}"?`)){await api('users/level/'+l.id+'/delete',{revision:usersData.revision});await usersPage();}}
  else if(b.dataset.userDelete){const u=usersData.users.find(x=>x.id===b.dataset.userDelete);if(confirm(`Delete user "${u.name}"? They are signed out immediately.`)){await api('users/user/'+u.id+'/delete',{revision:usersData.revision});await usersPage();}}
 }catch(err){error(err);}
});
