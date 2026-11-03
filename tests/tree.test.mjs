import assert from 'node:assert/strict';
import {GroupTree} from '../ui/tree.js';
const tree = new GroupTree(null,()=>{},()=>{},()=>{}); tree.render=()=>{};
const groups=[
  {id:'master',name:'全部声音',parentId:null,category:'音效',containsChildren:true,hasPcm:true},
  {id:'music',name:'音乐',parentId:'master',category:'音乐',hasPcm:true},
  {id:'voice',name:'对白',parentId:'master',category:'语音',hasPcm:true},
  {id:'steps',name:'脚步',parentId:'master',category:'音效',hasPcm:true}
];
tree.update(groups,{groups:{}});
assert.equal(tree.nodes.get('music').parent,'category:音乐');
assert.equal(tree.nodes.get('voice').parent,'category:语音');
assert.equal(tree.nodes.get('steps').parent,'category:音效');
assert.equal(tree.nodes.get('master').parent,'category:混音总线');
assert.equal(tree.nodes.get('category:音乐').children.length,1);
tree.setMode('hierarchy');
assert.equal(tree.nodes.get('music').parent,'master');
assert.equal(tree.nodes.get('master').children.length,3);
tree.setMode('category');
tree.update(groups,{folders:[{id:'custom',name:'我的声音'}],groups:{steps:{folderId:'custom',name:'我的脚步'}}});
assert.equal(tree.nodes.get('steps').parent,'custom');
assert.equal(tree.nodes.get('steps').name,'我的脚步');
console.log('PASS: category browsing keeps music and voice visible, original hierarchy and user folders remain available');

const previousDocument=globalThis.document;
const element=()=>({children:[],dataset:{},classList:{toggle(){}},setAttribute(){},append(...items){this.children.push(...items);},replaceChildren(){this.children=[];}});
try{
  globalThis.document={createElement:element};
  const rendered=new GroupTree(element(),()=>{},()=>{},()=>{});
  rendered.mode='hierarchy';rendered.update(groups,{groups:{master:{enabled:true},music:{enabled:true},voice:{enabled:false},steps:{enabled:false}}});
  rendered.expanded.add('category:混音总线');rendered.render();
  const checkbox=rendered.rows.get('master').row.children[1];
  assert.equal(checkbox.indeterminate,true);assert.equal(checkbox.checked,false);
  assert.equal(rendered.preset.groups.master.enabled,true);
  console.log('PASS: hierarchy renders a mixed checkbox without rewriting the enabled parent mix');
}finally{if(previousDocument===undefined)delete globalThis.document;else globalThis.document=previousDocument;}
