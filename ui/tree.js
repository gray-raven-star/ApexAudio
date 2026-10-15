export class GroupTree {
  constructor(element, change, select, editFolder) {
    this.element = element; this.change = change; this.select = select; this.editFolder = editFolder;
    this.expanded = new Set(); this.rows = new Map(); this.selected = null; this.filter = ''; this.audible = null; this.signature = ''; this.mode = 'category';
  }
  update(groups, preset) {
    const signature = JSON.stringify([groups, preset, this.mode]);
    if (signature === this.signature) return;
    this.signature = signature; this.groups = groups || []; this.preset = preset;
    this.nodes = new Map();
    for (const folder of preset?.folders || []) this.nodes.set(folder.id, {...folder, kind:'folder', parent:folder.parentId, children:[]});
    for (const g of this.groups) {
      const setting = preset?.groups[g.id] || {};
      const categoryName = this.mode === 'category' && g.containsChildren ? '混音总线' : g.category;
      const category = 'category:' + categoryName;
      if (!this.nodes.has(category)) this.nodes.set(category, {id:category, name:categoryName, kind:'category', parent:null, children:[]});
      this.nodes.set(g.id, {id:g.id, name:setting.name || g.name, parent:setting.folderId || (this.mode === 'hierarchy' ? g.parentId : null) || category, kind:'group', group:g, order:setting.order || 0, children:[]});
    }
    for (const node of this.nodes.values()) if (node.parent && this.nodes.has(node.parent)) this.nodes.get(node.parent).children.push(node);
    this.roots = [...this.nodes.values()].filter(n => !n.parent).filter(n => n.kind !== 'category' || n.children.length);
    const categories = ['音效','音乐','语音','环境','混响','其他','混音总线'];
    if (this.mode === 'category') this.roots.sort((a,b) => (categories.indexOf(a.name) < 0 ? 6 : categories.indexOf(a.name)) - (categories.indexOf(b.name) < 0 ? 6 : categories.indexOf(b.name)));
    for (const node of this.nodes.values()) node.children.sort((a,b) => (a.order || 0) - (b.order || 0) || a.name.localeCompare(b.name,'zh-CN'));
    if (!this.initialized) { this.roots.filter(n => n.id !== 'category:混音总线').forEach(n => this.expanded.add(n.id)); this.initialized = true; }
    this.render();
  }
  reset() { this.expanded.clear(); this.initialized = false; this.signature = ''; this.selected = null; }
  setMode(mode) { if (mode !== this.mode) { this.mode = mode; this.expanded.clear(); this.initialized = false; this.update(this.groups, this.preset); } }
  descendants(node) { return [node, ...node.children.flatMap(n => this.descendants(n))]; }
  setFilter(text, audible) { this.filter = text.trim().toLowerCase(); this.audible = audible; this.render(); }
  render() {
    if (!this.groups?.length) return;
    this.rows.clear(); this.element.replaceChildren();
    const keep = new Set();
    if (this.filter || this.audible) {
      for (const n of this.nodes.values()) {
        if (n.kind !== 'group') continue;
        const text = [n.name,n.id,n.group.category].join(' ').toLowerCase();
        if (this.filter && !text.includes(this.filter)) continue;
        if (this.audible && !this.audible.has(n.id)) continue;
        let current = n;
        while (current) { keep.add(current.id); current = current.parent ? this.nodes.get(current.parent) : null; }
      }
    }
    const filtered = !!(this.filter || this.audible);
    const append = (node, host) => {
      if (filtered && !keep.has(node.id)) return;
      const row = document.createElement('div'); row.className = 'tree-row' + (node.id === this.selected ? ' selected' : ''); row.dataset.id = node.id;
      const twist = document.createElement('button'); twist.className = 'twisty'; twist.textContent = node.children.length ? (this.expanded.has(node.id) || filtered ? '▾' : '▸') : '·';
      twist.setAttribute('aria-label', '展开或收起 ' + node.name);
      const checkbox = document.createElement('input'); checkbox.type = 'checkbox';
      const members = this.descendants(node).filter(n => n.kind === 'group' && n.group.hasPcm);
      const count = members.filter(n => this.preset?.groups[n.id]?.enabled).length;
      checkbox.checked = members.length > 0 && count === members.length; checkbox.indeterminate = count > 0 && count < members.length;
      checkbox.disabled = !members.length; checkbox.setAttribute('aria-label', '参与反馈：' + node.name);
      const name = document.createElement('span'); name.className = 'tree-name'; name.textContent = node.name; name.title = node.name;
      row.append(twist, checkbox, name);
      if (node.kind === 'group') {
        const id = document.createElement('span'); id.className = 'tree-id'; id.textContent = node.group.nameSource === '编号' ? node.id : ''; row.append(id);
        const meter = document.createElement('span'); meter.className = 'level-mini'; meter.append(document.createElement('i')); row.append(meter);
      } else {
        const count = document.createElement('span'); count.className = 'tree-count'; count.textContent = members.length; row.append(count);
        if (node.kind === 'folder') { const edit = document.createElement('button'); edit.className = 'twisty'; edit.textContent = '⋯'; edit.title = '编辑文件夹'; edit.onclick = e => { e.stopPropagation(); this.editFolder(node); }; row.append(edit); }
      }
      twist.onclick = e => { e.stopPropagation(); if (this.expanded.has(node.id)) this.expanded.delete(node.id); else this.expanded.add(node.id); this.render(); };
      checkbox.onclick = e => e.stopPropagation(); checkbox.onchange = () => {
        if (node.kind === 'group') this.change('group', node.id, checkbox.checked);
        else if (node.kind === 'folder') this.change('folderToggle', node.id, checkbox.checked);
        else members.forEach(n => this.change('group', n.id, checkbox.checked));
      };
      row.onclick = () => { if (node.kind === 'group') { this.setSelected(node.id); this.select(node.group); } else twist.click(); };
      this.rows.set(node.id, {row, node}); host.append(row);
      if (node.children.length && (this.expanded.has(node.id) || filtered)) { const children = document.createElement('div'); children.className = 'tree-children'; host.append(children); node.children.forEach(n => append(n, children)); }
    };
    this.roots.forEach(n => append(n, this.element));
    if (!this.element.children.length) { const p = document.createElement('p'); p.className = 'empty muted'; p.textContent = '没有符合条件的分组'; this.element.append(p); }
  }
  setSelected(id) { this.selected = id; for (const [key, entry] of this.rows) entry.row.classList.toggle('selected', key === id); }
  activity(levels) {
    for (const {row,node} of this.rows.values()) {
      const meter = row.querySelector('.level-mini i'); if (meter) meter.style.width = Math.min(100,(levels[node.id] || 0)*100) + '%';
    }
  }
  collapse() { this.expanded.clear(); this.roots?.filter(n => n.id !== 'category:混音总线').forEach(n => this.expanded.add(n.id)); this.render(); }
}
