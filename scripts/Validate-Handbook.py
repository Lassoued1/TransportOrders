from pathlib import Path
import json
import re
import uuid
from pypdf import PdfReader
import pdfplumber
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parent.parent
rendered = root / 'tmp' / 'pdfs'
report = []
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 15)
for pdf in sorted((root / 'output' / 'pdf').glob('*.pdf')):
    reader = PdfReader(pdf)
    language = pdf.stem.rsplit('-', 1)[1].lower()
    kind = 'technical' if '-TECHNICAL-' in pdf.stem else 'user'
    source = root / 'docs' / 'handbook' / f'{kind}.{language}.md'
    headings = [line[3:] for line in source.read_text(encoding='utf-8').splitlines() if line.startswith('## ')]
    outline = [entry for entry in reader.outline if not isinstance(entry, list)]
    assert len(outline) == len(headings), (pdf.name, 'outline count')
    assert [entry.title for entry in outline] == headings, (pdf.name, 'outline headings')
    for entry in outline:
        index = reader.get_destination_page_number(entry)
        assert entry.title in reader.pages[index].extract_text(), (pdf.name, entry.title, 'incorrect destination')
    text = '\n'.join(page.extract_text() for page in reader.pages)
    assert '\ufffd' not in text and 'Placeholder for table of contents' not in text
    assert len(text) > 8000, (pdf.name, 'unexpectedly short document')
    overflow = []
    with pdfplumber.open(pdf) as document:
        for number, page in enumerate(document.pages, 1):
            for char in page.chars:
                if char['text'].strip() and (char['x0'] < 43 or char['x1'] > page.width-43
                    or char['top'] < 15 or char['bottom'] > page.height-15):
                    overflow.append((number, char['text'], char['x0'], char['x1']))
    assert not overflow, (pdf.name, 'page overflow', overflow[:6])
    images = sorted(rendered.glob(pdf.stem + '-*.png'), key=lambda p: int(p.stem.rsplit('-',1)[1]))
    assert len(images) == len(reader.pages), (pdf.name, 'missing rendered pages')
    sheet = Image.new('RGB', (1000, ((len(images)+2)//3)*485), '#dce4ec')
    draw = ImageDraw.Draw(sheet)
    for index, page in enumerate(images):
        image = Image.open(page).convert('RGB')
        image.thumbnail((308, 438))
        x, y = 12+(index%3)*330, 30+(index//3)*485
        sheet.paste(image, (x,y))
        draw.text((x,y-21), f'{pdf.stem} / {index+1}', fill='#17324d', font=font)
    preview = rendered / (pdf.stem + '-review-' + uuid.uuid4().hex[:8] + '.jpg')
    sheet.save(preview, quality=90)
    report.append({'file': pdf.name, 'pages': len(reader.pages), 'sections': len(headings), 'characters': len(text),
                   'outline_valid': True, 'page_bounds_valid': True, 'rendered_pages': len(images), 'preview': str(preview)})

for markdown in (root/'docs'/'handbook').glob('*.md'):
    for target in re.findall(r'\[[^\]]+\]\(([^)]+)\)', markdown.read_text(encoding='utf-8')):
        if not target.startswith(('http:', 'https:', '#')):
            assert (markdown.parent / target).exists(), (markdown.name, target, 'broken link')
(root/'artifacts'/'handbook-validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report, indent=2))
