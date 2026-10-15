"""Create the release's original geometric icon; no driver artwork is reused."""
from pathlib import Path
from PIL import Image, ImageDraw

destination = Path(__file__).parent / 'assets'
destination.mkdir(exist_ok=True)
image = Image.new('RGBA', (256, 256), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((8, 8, 248, 248), radius=54, fill='#101b2b')
draw.line([(43, 130), (74, 130), (94, 77), (120, 181), (145, 58), (169, 150), (184, 117), (213, 117)], fill='#4b8dff', width=14, joint='curve')
image.save(destination / 'apex.ico', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
(destination / 'apex.svg').write_text('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256"><rect x="8" y="8" width="240" height="240" rx="54" fill="#101b2b"/><path d="M43 130H74L94 77L120 181L145 58L169 150L184 117H213" fill="none" stroke="#4b8dff" stroke-width="14" stroke-linejoin="round" stroke-linecap="round"/></svg>\n', encoding='utf-8')
