"""Bundle the local pilot without copying live user data or the Flydigi engine."""
from pathlib import Path
import hashlib, json, shutil, zipfile

root = Path(__file__).resolve().parents[1]
target = root.parent / 'ApexAudio-V2-0.2.0-试用版'
if target.exists():
    raise SystemExit('Trial output already exists; choose a new destination.')
shutil.copytree(root / 'dist', target, ignore=shutil.ignore_patterns('*.pdb'))
packages = target / '试用包'
shutil.copytree(root / 'artifacts/v2-packages', packages)
docs = target / '开发说明'
docs.mkdir()
shutil.copy2(root / 'docs/development/v2-trial.md', docs / 'V2技术说明.md')
examples = docs / '示例定义'
for pack in packages.glob('*.gpack'):
    with zipfile.ZipFile(pack) as z:
        z.extractall(examples / pack.stem)

# The bridge is identical to the installed, previously supported one. Carry only
# its public compatibility list, never engine binaries, tokens or user settings.
live = Path(r'E:\APEX6pro\ApexAudio\native')
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
if digest(target/'native/ApexGroupsBridge-v5.dll') == digest(live/'ApexGroupsBridge-v5.dll'):
    for name in ['xh-engines.json', 'xh-support.json']:
        shutil.copy2(live/name, target/'native'/name)
(target/'data').mkdir()
settings = dict(autoConnect=True, autoFeedback=False, autoStartXh=True, port=18757)
engine = Path(r'E:\APEX6pro\Engine\xHaptics Engine.exe')
if engine.exists():
    settings['xhExecutable'] = str(engine)
(target/'data/settings.json').write_text(json.dumps(settings, ensure_ascii=False, indent=2), encoding='utf-8')
(target/'README.md').write_text('''# ApexAudio V2 0.2.0 试用版

这次先测试一件事：两款游戏共用一份 FMOD 采集后端，游戏包只保存配置和预设。
已确认天国：拯救 2、杀戮尖塔 2 均能采到真实声音；尚未验证手柄体感和完整游戏场景。

## 怎么试

1. 正常退出原来那份 ApexAudio，运行本目录的 ApexAudio.exe。
2. 进入“游戏适配包”，点击“导入适配包”，先选“试用包”里的 ApexAudio-Fmod-0.2.0.gbackend。
3. 再用同一个按钮导入需要的游戏 .gpack。共享后端只导入一次。
4. 打开对应游戏，在“声音工作台”选包，确认连接后尝试音轨开关、录音和试听。
5. 如需测试震动，保持飞智模式，打开“X-Haptics 补充震动”。本机原有 Engine 路径已配置；本包没有附带或替换飞智引擎。

本目录使用独立 data，不会替换现用程序、录音和预设。示例包包含“全部声音”和从本机现有预设带来的方案；后续调整保存在本试用版自己的 data 里。
首次试用时不要把旧 data 整个复制过来。V1 包仍可导入，但现有用户预设尚未提供自动 V1→V2 迁移。
测试期间避免两份 ApexAudio 同时连接同一游戏。如游戏还加载着旧采集模块，可正常退出后重新启动游戏。

## 给开发者

“开发说明”包含技术说明和两个可直接修改的配置目录。打包不需要编译采集 DLL，例如：

```powershell
.\\tools\\PackV2\\ApexAudio.PackV2.exe "定义目录" "输出.gpack"
```

“tools/DevTools”提供包检查工具。共享后端本身仍包含可执行代码；应确认来源后导入。配置摘要用于完整性检查，不代表作者身份认证。

这轮仅做 FMOD 2.02/2.03 试点。Wwise/Unreal 共享后端、在线目录、自动迁移和历史版本回退尚未实现。社区入口等试用确认后再推进。
''', encoding='utf-8')
report = '''# 本地验证记录 · 2026-10-31

- 原有自动验收：44 项通过，涵盖波形、独立录音、预设、重连、反馈排队、包校验等。
- 前端测试：3 项通过。
- 界面自动检查：启动、模拟录音、定时停止、倒计时取消和深浅色界面通过。
- V2 专项：18 项通过，涵盖配置包导入、同一共享后端、身份/上下文、稳定分组 ID、篡改拒绝、错误目标拒绝及原 V1 包导入启动。
- 作者工具：示例定义重新打包成功，产物通过完整性检查。
- 共享后端构建脚本：独立构建成功。
- 天国：拯救 2：五秒 468 个音频包，2 个非零音轨，丢包和错误为 0。
- 杀戮尖塔 2：五秒 1413 个音频包，3 个非零音轨，丢包和错误为 0。

两次真实采集使用同一 .gbackend，游戏差异均由配置表达。未启动手柄输出测试，标题画面采集不等于所有游戏场景均已验证。

过程中的实际故障：首次杀戮尖塔 2 测试发生游戏访问冲突。排查发现共享代码沿用 FMOD 2.02 的 System_GetVersion 两参数调用，而 2.03 需要额外的输出参数。按已验证接口系列分开调用后，先在独立无声进程验证两种版本查询，再重新打开游戏完成上述非零 PCM 采集。没有用反复改游戏参数来掩盖这个接口问题。
'''
passes = sum(line.startswith('PASS:') for line in (root/'artifacts/v2-acceptance-build.log').read_text(encoding='utf-8-sig').splitlines())
report = report.replace('44 项通过', f'{passes} 项通过')
(docs/'验证记录.md').write_text(report, encoding='utf-8')
archive = Path(str(target) + '.zip')
if archive.exists():
    raise SystemExit('Archive output already exists.')
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(target.rglob('*')):
        if p.is_file():
            z.write(p, p.relative_to(target.parent))
with zipfile.ZipFile(archive) as z:
    if z.testzip() is not None:
        raise RuntimeError('Archive verification failed.')
print(target)
print(archive)
print('ZIP bytes:', archive.stat().st_size)
print('SHA256:', digest(archive).upper())
