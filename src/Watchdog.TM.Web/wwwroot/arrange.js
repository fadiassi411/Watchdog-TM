'use strict';
// V4.6.0: arrange the temperature blocks on the dashboard. Drag a block (mouse), or use the arrow buttons
// (touch screens and keyboard). The order is saved on the server and shown on every screen.
let arrangeRevision='',draggedCard=null;
const arrangeGrid=()=>$('content').querySelector('.grid');

function startArranging(){
 const grid=arrangeGrid();if(!grid)return;
 arranging=true;arrangeRevision=state.revision;grid.classList.add('arranging');
 grid.insertAdjacentHTML('beforebegin',`<div class="panel arrange-panel" id="arrangePanel"><strong>Arrange the temperature blocks</strong><p class="muted">Drag a block to its new place, or use the arrows. Live updates pause until you save or cancel.</p><div class="actions"><button data-arrange-save>Save layout</button><button class="secondary" data-arrange-reset>Reset to default order</button><button class="secondary" data-arrange-cancel>Cancel</button></div><p id="arrangeError" class="error-text" role="alert"></p></div>`);
 for(const card of grid.querySelectorAll('.temperature-card')){
  card.draggable=true;
  card.insertAdjacentHTML('afterbegin',`<div class="arrange-bar"><button type="button" class="secondary" data-move="-1" aria-label="Move ${esc(card.querySelector('h2')?.textContent)} earlier">◀</button><span>⠿ Drag to move</span><button type="button" class="secondary" data-move="1" aria-label="Move ${esc(card.querySelector('h2')?.textContent)} later">▶</button></div>`);
 }
 const start=$('content').querySelector('[data-arrange-start]');if(start)start.hidden=true;
}
function stopArranging(){arranging=false;draggedCard=null;render();}

$('content').addEventListener('click',async e=>{
 const b=e.target.closest('button');if(!b)return;
 if(b.hasAttribute('data-arrange-start')){startArranging();return;}
 if(!arranging)return;
 if(b.dataset.move){const card=b.closest('.temperature-card'),grid=arrangeGrid();if(b.dataset.move==='-1'&&card.previousElementSibling)grid.insertBefore(card,card.previousElementSibling);else if(b.dataset.move==='1'&&card.nextElementSibling)grid.insertBefore(card.nextElementSibling,card);b.focus();return;}
 if(b.hasAttribute('data-arrange-cancel')){stopArranging();return;}
 const save=b.hasAttribute('data-arrange-save'),reset=b.hasAttribute('data-arrange-reset');if(!save&&!reset)return;
 if(reset&&!confirm('Put the temperature blocks back in the default order for every screen?'))return;
 b.disabled=true;
 try{
  const sensors=[...arrangeGrid().querySelectorAll('.temperature-card')].map(c=>c.dataset.sensor);
  await api('dashboard/order',{revision:arrangeRevision,sensors,reset});
  arranging=false;draggedCard=null;await refresh();error(reset?'Default order restored.':'Dashboard layout saved for every screen.');
 }catch(err){$('arrangeError').textContent=err.message;b.disabled=false;}
});
$('content').addEventListener('dragstart',e=>{if(!arranging)return;draggedCard=e.target.closest('.temperature-card');if(!draggedCard)return;draggedCard.classList.add('dragging');e.dataTransfer.effectAllowed='move';e.dataTransfer.setData('text/plain',draggedCard.dataset.sensor);});
$('content').addEventListener('dragover',e=>{
 if(!arranging||!draggedCard)return;e.preventDefault();
 const target=e.target.closest('.temperature-card');if(!target||target===draggedCard)return;
 const cards=[...arrangeGrid().children];
 if(cards.indexOf(draggedCard)<cards.indexOf(target))target.after(draggedCard);else target.before(draggedCard);
});
$('content').addEventListener('drop',e=>{if(arranging)e.preventDefault();});
$('content').addEventListener('dragend',()=>{draggedCard?.classList.remove('dragging');draggedCard=null;});
window.addEventListener('hashchange',()=>{arranging=false;draggedCard=null;});
