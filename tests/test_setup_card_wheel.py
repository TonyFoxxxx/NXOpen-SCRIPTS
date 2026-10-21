"""Exercise the actual HTML wheel handlers without NX or third-party packages."""
import json
from pathlib import Path
import shutil
import subprocess
import unittest

SOURCE = Path(__file__).resolve().parents[1] / 'scripts/NX_Setup_Prototype.py'
NODE = shutil.which('node')


@unittest.skipUnless(NODE, 'Node.js is required to execute the embedded HTML handlers')
class WheelScaleTests(unittest.TestCase):
    def test_table_steps_isolation_limits_and_serialization(self):
        program = r'''
const assert=require('node:assert/strict'), vm=require('node:vm');
const src=require('node:fs').readFileSync(SOURCE,'utf8');
const helper=src.slice(src.indexOf('const WHEEL_SCALE_STEP ='),src.indexOf('// Edit the displayed percentage'));
assert(helper.includes('function wheelScaleDirection'));
const pageScale=src.slice(src.indexOf('const PageScale=(()=>{'),src.indexOf('// Collapse belongs to one tools list:'));
const handlers={},context={edits:0};
const scope={dataset:{},querySelectorAll:()=>[],querySelector:()=>context.page,contains:p=>p===context.page,addEventListener:(name,fn)=>handlers[name]=fn};
Object.assign(context,{scope,local:()=>null,ColumnOptions:{wrap:()=>false},CardFlow:{reflow:()=>{}},markChanged:()=>context.edits++,window:{scrollBy:()=>{}}});
vm.createContext(context);vm.runInContext(helper+pageScale+';globalThis.api=PageScale;',context);
function send(family,key,deltaY,ctrlKey=true,deltaX=0){
 context.page={dataset:{pageKey:key},getBoundingClientRect:()=>({left:0,top:0})};
 const table={dataset:{fitFamily:family},closest:()=>context.page};
 const e={target:{closest:()=>table},deltaY,deltaX,ctrlKey,defaultPrevented:false,preventDefault(){this.defaultPrevented=true;}};
 handlers.wheel(e);return e;
}
const api=context.api;
api.restore({cover:100,catalog:150,'operations-1':125},null,90,{});
assert(send('operations','cover',-120).defaultPrevented);assert.equal(api.value('cover'),105);assert.equal(api.value('catalog'),150);assert.equal(api.toolsValue(),90);
send('operations','cover',-600);assert.equal(api.value('cover'),110);send('operations','cover',120);assert.equal(api.value('cover'),105);
send('cover-tools','cover',-120);assert.equal(api.toolsValue(),95);assert.equal(api.value('cover'),105);
send('project-tools','catalog',-120);assert.equal(api.value('catalog'),155);assert.equal(api.value('operations-1'),125);
send('operations','operations-1',120);assert.equal(api.value('operations-1'),120);assert.equal(api.value('cover'),105);
for(const [y,ctrl,x] of [[120,false,0],[0,true,0],[10,true,100],[NaN,true,0]]){const before=JSON.stringify(api.state()),tools=api.toolsValue();assert(!send('operations','cover',y,ctrl,x).defaultPrevented);assert.equal(JSON.stringify(api.state()),before);assert.equal(api.toolsValue(),tools);}
api.restore({cover:300,catalog:60},null,300,{});let edits=context.edits;assert(send('operations','cover',-120).defaultPrevented);assert(send('project-tools','catalog',120).defaultPrevented);assert(send('cover-tools','cover',-120).defaultPrevented);assert.equal(context.edits,edits);
const copy={dataset:{},querySelectorAll:()=>[]};api.save(copy);api.restore({cover:100},null,100,{});api.restore(JSON.parse(copy.dataset.pageScales),null,copy.dataset.coverToolScale,JSON.parse(copy.dataset.pageWraps));assert.equal(api.value('cover'),300);assert.equal(api.value('catalog'),60);assert.equal(api.toolsValue(),300);
'''
        subprocess.run([NODE, '-e', 'const SOURCE=' + json.dumps(str(SOURCE)) + ';\n' + program], check=True, capture_output=True, text=True)

    def test_model_steps_do_not_depend_on_delta_size_or_unit(self):
        program = r'''
const assert=require('node:assert/strict'),vm=require('node:vm');
const src=require('node:fs').readFileSync(SOURCE,'utf8');
const helper=src.slice(src.indexOf('const WHEEL_SCALE_STEP ='),src.indexOf('// Edit the displayed percentage'));
const start=src.indexOf("document.addEventListener('wheel',event=>{",src.indexOf('const ProjectModel='));
const end=src.indexOf('},{passive:false});',start)+'},{passive:false});'.length;
let listener,scale=100;
const context={active:true,hit:()=>true,finish:()=>{},commit:()=>{},queue:()=>{},ProjectImage:{value:()=>scale,set:n=>scale=n},document:{addEventListener:(type,fn)=>listener=fn}};
vm.createContext(context);vm.runInContext(helper+src.slice(start,end),context);
for(const deltaMode of [0,1,2])for(const size of [1,3,100,120,600]){
 scale=100;let prevented=false;const e={deltaY:-size,deltaX:0,deltaMode,preventDefault:()=>prevented=true};listener(e);assert.equal(scale,105);assert(prevented);listener(e);assert.equal(scale,110);listener({...e,deltaY:size});assert.equal(scale,105);
}
for(const e of [{deltaY:0,deltaX:0},{deltaY:10,deltaX:100},{deltaY:NaN,deltaX:0}]){scale=100;listener({...e,preventDefault(){throw Error('Unexpected cancellation');}});assert.equal(scale,100);}
'''
        subprocess.run([NODE, '-e', 'const SOURCE=' + json.dumps(str(SOURCE)) + ';\n' + program], check=True, capture_output=True, text=True)


if __name__ == '__main__':
    unittest.main()
