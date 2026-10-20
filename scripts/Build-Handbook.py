"""Build the six handbook PDFs from their maintained Markdown sources."""
from pathlib import Path
import re
from html import escape
from reportlab.pdfgen import canvas
from reportlab.platypus import BaseDocTemplate, PageTemplate, Frame, Paragraph, Spacer, PageBreak, Table, TableStyle, Preformatted, KeepTogether
from reportlab.platypus.tableofcontents import TableOfContents
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'docs' / 'handbook'
OUTPUT = ROOT / 'output' / 'pdf'
OUTPUT.mkdir(parents=True, exist_ok=True)
fonts = Path('C:/Windows/Fonts')
for name, file in [('Doc', 'arial.ttf'), ('DocBold', 'arialbd.ttf'), ('DocItalic', 'ariali.ttf'), ('DocMono', 'consola.ttf')]:
    pdfmetrics.registerFont(TTFont(name, str(fonts / file)))
pdfmetrics.registerFontFamily('Doc', normal='Doc', bold='DocBold', italic='DocItalic', boldItalic='DocBold')
NAVY = colors.HexColor('#17324D')
BLUE = colors.HexColor('#245C86')
MUTED = colors.HexColor('#617183')
LIGHT = colors.HexColor('#EEF3F7')
INK = colors.HexColor('#202F3C')
WIDTH = A4[0] - 96
styles = {
    'body': ParagraphStyle('body', fontName='Doc', fontSize=9.4, leading=13.8, textColor=INK, spaceAfter=7, splitLongWords=True, allowWidows=0, allowOrphans=0),
    'h2': ParagraphStyle('h2', fontName='DocBold', fontSize=14.3, leading=18, textColor=NAVY, spaceBefore=14, spaceAfter=8, keepWithNext=True),
    'cell': ParagraphStyle('cell', fontName='Doc', fontSize=8.0, leading=11.3, textColor=INK, splitLongWords=True),
    'thead': ParagraphStyle('thead', fontName='DocBold', fontSize=8.2, leading=11.5, textColor=colors.white, splitLongWords=True),
    'list': ParagraphStyle('list', fontName='Doc', fontSize=9.4, leading=13.8, textColor=INK, leftIndent=13, firstLineIndent=-13, spaceAfter=5),
    'meta': ParagraphStyle('meta', fontName='Doc', fontSize=10, leading=15, textColor=MUTED, spaceAfter=10),
    'cover': ParagraphStyle('cover', fontName='DocBold', fontSize=27, leading=33, textColor=NAVY, spaceAfter=12),
    'subtitle': ParagraphStyle('subtitle', fontName='DocBold', fontSize=20, leading=26, textColor=BLUE, spaceAfter=20),
    'toc': ParagraphStyle('toc', fontName='Doc', fontSize=10.1, leading=16, textColor=INK, spaceBefore=5, leftIndent=0),
}

def inline(value):
    value = escape(value)
    value = re.sub(r'`([^`]+)`', lambda m: '<font name="DocMono" size="8.2">' + m.group(1) + '</font>', value)
    value = re.sub(r'\*\*([^*]+)\*\*', r'<b>\1</b>', value)
    return value

class Handbook(BaseDocTemplate):
    def __init__(self, path, language, kind, title):
        super().__init__(str(path), pagesize=A4, leftMargin=48, rightMargin=48, topMargin=60, bottomMargin=48,
                         title=title, author='TransportOrders project', subject=kind, pageCompression=1)
        self.language, self.kind, self.doc_title = language, kind, title
        self.addPageTemplates(PageTemplate(id='handbook', frames=[Frame(48, 48, WIDTH, A4[1]-108, leftPadding=0,
                              rightPadding=0, topPadding=0, bottomPadding=0)], onPage=self.decorate))
    def decorate(self, c, doc):
        c.saveState()
        c.setFont('DocBold', 8); c.setFillColor(NAVY)
        c.drawString(48, A4[1]-32, 'TRANSPORTORDERS')
        c.setFont('Doc', 8); c.setFillColor(MUTED)
        c.drawRightString(A4[0]-48, A4[1]-32, self.kind + ' | ' + self.language.upper())
        c.setStrokeColor(colors.HexColor('#D7E1EA')); c.line(48, A4[1]-40, A4[0]-48, A4[1]-40)
        c.line(48, 37, A4[0]-48, 37)
        c.drawString(48, 24, ('Ausgabe' if self.language == 'de' else 'Edition') + ' 1.0 | 12.10.2026')
        c.drawRightString(A4[0]-48, 24, ('Seite' if self.language == 'de' else 'Page') + ' ' + str(doc.page))
        c.restoreState()
    def afterFlowable(self, flowable):
        if isinstance(flowable, Paragraph) and flowable.style.name == 'h2':
            text = flowable.getPlainText()
            key = 'section-' + str(self.seq.nextf('section'))
            self.canv.bookmarkPage(key)
            self.canv.addOutlineEntry(text, key, 0, False)
            self.notify('TOCEntry', (0, text, self.page, key))

def build(source):
    lines = source.read_text(encoding='utf-8').splitlines()
    language = source.stem.split('.')[-1]
    kind = source.stem.split('.')[0]
    styles['body'].leading = 13.0 if kind == 'technical' else 13.8
    styles['body'].spaceAfter = 6 if kind == 'technical' else 7
    title = lines[0].removeprefix('# ')
    labels = {
        'en': ('Contents', 'Reference for the current source and packages. Verify the installed service version before use.'),
        'de': ('Inhalt', 'Referenz zum aktuellen Quellcode und Paketstand. Den installierten Dienststand vor Verwendung prüfen.'),
        'fr': ('Sommaire', 'Référence du code et des paquets actuels. Vérifier la version du service installé avant utilisation.'),
    }
    target = OUTPUT / f'TransportOrders-{kind.upper()}-{language.upper()}.pdf'
    story = [Spacer(1, 24), Paragraph('TransportOrders', styles['cover']),
             Paragraph(escape(title.split(' - ', 1)[1]), styles['subtitle']),
             Paragraph(escape(lines[2]), styles['meta']),
             Paragraph(labels[language][1], styles['body']), Spacer(1, 22),
             Paragraph(labels[language][0], ParagraphStyle('contents-title', parent=styles['h2']))]
    toc = TableOfContents(); toc.levelStyles = [styles['toc']]; story += [toc, PageBreak()]
    i = 3
    while i < len(lines):
        line = lines[i].strip()
        if not line: i += 1; continue
        if line.startswith('## '):
            story.append(Paragraph(inline(line[3:]), styles['h2'])); i += 1; continue
        if line.startswith('```'):
            i += 1; code = []
            while i < len(lines) and not lines[i].startswith('```'): code.append(lines[i]); i += 1
            i += 1
            size = min(8.0, (WIDTH-18)/(max(map(len, code), default=1)*0.61))
            cs = ParagraphStyle('code', fontName='DocMono', fontSize=size, leading=size*1.5, textColor=NAVY)
            block = Preformatted('\n'.join(code), cs)
            story += [Table([[block]], colWidths=[WIDTH], style=TableStyle([
                ('BACKGROUND', (0,0),(-1,-1), LIGHT), ('LEFTPADDING',(0,0),(-1,-1),9),
                ('RIGHTPADDING',(0,0),(-1,-1),9), ('TOPPADDING',(0,0),(-1,-1),8),
                ('BOTTOMPADDING',(0,0),(-1,-1),8)])), Spacer(1, 10)]
            continue
        if line.startswith('|'):
            rows = []
            while i < len(lines) and lines[i].strip().startswith('|'):
                parts = [p.strip() for p in lines[i].strip().strip('|').split('|')]
                if not all(re.fullmatch(r':?-+:?', p) for p in parts): rows.append(parts)
                i += 1
            count = len(rows[0]); ratios = [0.41, 0.59] if count == 2 else [0.30, 0.31, 0.39]
            if any('/api/' in c for c in rows[1][0:1]): ratios = [0.57, 0.43]
            data = [[Paragraph(inline(c), styles['thead' if r == 0 else 'cell']) for c in row] for r, row in enumerate(rows)]
            table = Table(data, colWidths=[WIDTH*x for x in ratios], repeatRows=1, hAlign='LEFT')
            table.setStyle(TableStyle([
                ('BACKGROUND',(0,0),(-1,0),NAVY), ('ROWBACKGROUNDS',(0,1),(-1,-1),[colors.white,LIGHT]),
                ('VALIGN',(0,0),(-1,-1),'TOP'), ('LEFTPADDING',(0,0),(-1,-1),7), ('RIGHTPADDING',(0,0),(-1,-1),7),
                ('TOPPADDING',(0,0),(-1,-1),5 if kind == 'technical' else 6), ('BOTTOMPADDING',(0,0),(-1,-1),5 if kind == 'technical' else 6),
                ('LINEBELOW',(0,0),(-1,0),0.5,BLUE), ('LINEBELOW',(0,-1),(-1,-1),0.4,colors.HexColor('#D7E1EA'))]))
            story += [table, Spacer(1, 10)]; continue
        if re.match(r'^(\d+\. |\- )', line):
            story.append(Paragraph(inline(line), styles['list'])); i += 1; continue
        paragraph = [line]; i += 1
        while i < len(lines) and lines[i].strip() and not lines[i].startswith(('## ', '|', '```')):
            paragraph.append(lines[i].strip()); i += 1
        story.append(Paragraph(inline(' '.join(paragraph)), styles['body']))
    doc = Handbook(target, language, title.split(' - ',1)[1], title)
    if kind == 'user':
        compact = []; group = []
        for flowable in story:
            is_heading = isinstance(flowable, Paragraph) and flowable.style.name == 'h2'
            if is_heading and group:
                compact.append(KeepTogether(group)); group = []
            if is_heading and flowable.getPlainText().startswith(('9.', '10.')):
                group = [flowable]
            elif group: group.append(flowable)
            else: compact.append(flowable)
        if group: compact.append(KeepTogether(group))
        story = compact
    doc.multiBuild(story)
    print(target)

if __name__ == '__main__':
    for source in sorted(SOURCE.glob('technical.*.md')) + sorted(SOURCE.glob('user.*.md')):
        build(source)
