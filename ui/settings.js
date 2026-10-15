const $ = id => document.getElementById(id);
export class SettingsPanel {
  constructor(send) {
    this.send=send; this.server={}; this.hotkey={};
    $('applySettings').onclick=()=>this.save();
    $('gain').oninput=()=>$('gainValue').textContent=Number($('gain').value).toFixed(2);
    $('recordingMode').onchange=()=>this.mode();
    $('theme').onchange=()=>{document.documentElement.dataset.theme=$('theme').value;send('theme',{theme:$('theme').value});};
    $('chooseXh').onclick=()=>send('chooseXh');
    $('learnController').onclick=()=>send('learnController');$('clearController').onclick=()=>send('clearController');
    $('keyboardHotkey').onfocus=()=>{send('keyboardLearn');$('keyboardHotkey').placeholder='按下要绑定的组合';};
    $('keyboardHotkey').onblur=()=>send('keyboardLearnEnd');
    $('keyboardHotkey').onkeydown=e=>{
      e.preventDefault();e.stopPropagation(); if(['Control','Alt','Shift','Meta'].includes(e.key))return;
      if(e.key==='Escape'){$('keyboardHotkey').blur();return;}
      const modifiers=(e.altKey?1:0)|(e.ctrlKey?2:0)|(e.shiftKey?4:0)|(e.metaKey?8:0);
      const name=[e.ctrlKey?'Ctrl':null,e.altKey?'Alt':null,e.shiftKey?'Shift':null,e.metaKey?'Win':null,e.key.length===1?e.key.toUpperCase():e.key].filter(Boolean).join(' + ');
      this.hotkey={hotkeyModifiers:modifiers,hotkeyKey:e.keyCode,hotkeyName:name};$('keyboardHotkey').value=name;
    };
    $('clearKeyboard').onclick=()=>{this.hotkey={hotkeyModifiers:0,hotkeyKey:0,hotkeyName:'未绑定'};$('keyboardHotkey').value='未绑定';};
  }
  update(settings) {
    if(!settings)return;const previous=this.server;
    const fields={gain:'gain',port:'port',autoConnect:'autoConnect',autoFeedback:'autoFeedback',autoStartXh:'autoStartXh',xhExecutable:'xhExecutable',recordingMode:'recordingMode',recordingSeconds:'recordingSeconds',recordingDelay:'recordingDelay',recordingSounds:'recordingSounds',theme:'theme',controllerName:'controllerHotkey'};
    for(const [key,id]of Object.entries(fields))if(previous[key]!==settings[key]){const el=$(id);if(el.type==='checkbox')el.checked=!!settings[key];else el.value=settings[key]??'';}
    if(previous.hotkeyKey!==settings.hotkeyKey||previous.hotkeyModifiers!==settings.hotkeyModifiers||!Object.keys(this.hotkey).length){this.hotkey={hotkeyModifiers:settings.hotkeyModifiers,hotkeyKey:settings.hotkeyKey,hotkeyName:settings.hotkeyName};$('keyboardHotkey').value=settings.hotkeyName;}
    this.server=settings;document.documentElement.dataset.theme=settings.theme;$('gainValue').textContent=Number($('gain').value).toFixed(2);this.mode();
  }
  mode(){$('recordingSeconds').disabled=$('recordingMode').value==='manual';}
  save(){
    const settings={...this.server,...this.hotkey,gain:Number($('gain').value),port:Number($('port').value),autoConnect:$('autoConnect').checked,autoFeedback:$('autoFeedback').checked,autoStartXh:$('autoStartXh').checked,xhExecutable:$('xhExecutable').value,theme:$('theme').value,recordingMode:$('recordingMode').value,recordingSeconds:Number($('recordingSeconds').value),recordingDelay:Number($('recordingDelay').value),recordingSounds:$('recordingSounds').checked};
    this.send('settings',{settings});
  }
  learned(message){$('hotkeyLearnStatus').hidden=false;$('hotkeyLearnStatus').textContent=message.message;$('learnController').disabled=message.listening;}
}
