"""Compose real WPF captures and German narration into a shareable MP4."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import argparse, json, subprocess, wave, textwrap, shutil

parser = argparse.ArgumentParser()
parser.add_argument('frames', type=Path)
args = parser.parse_args()
source = args.frames.resolve()
root = Path(__file__).resolve().parent.parent
output = root / 'output' / 'video'
output.mkdir(parents=True, exist_ok=True)
scenes = json.loads((source / 'scenes.json').read_text(encoding='utf-8'))
fonts = Path('C:/Windows/Fonts')
regular = lambda size: ImageFont.truetype(str(fonts / 'arial.ttf'), size)
bold = lambda size: ImageFont.truetype(str(fonts / 'arialbd.ttf'), size)

def lines(draw, text, font, width):
    result = []; line = ''
    for word in text.split():
        candidate = (line + ' ' + word).strip()
        if draw.textlength(candidate, font=font) > width and line:
            result.append(line); line = word
        else: line = candidate
    if line: result.append(line)
    return result

audio_data = []
format_info = None
for scene in scenes:
    wav = source / (Path(scene['File']).stem + '.wav')
    with wave.open(str(wav), 'rb') as reader:
        fmt = (reader.getnchannels(), reader.getsampwidth(), reader.getframerate())
        if format_info is None: format_info = fmt
        assert format_info == fmt, 'Narration audio formats must match'
        data = reader.readframes(reader.getnframes())
        seconds = reader.getnframes() / reader.getframerate()
    scene['Seconds'] = max(scene['Seconds'], seconds + 0.8)
    padding_frames = round(scene['Seconds'] * fmt[2]) - len(data) // (fmt[0] * fmt[1])
    audio_data.append(data + b'\x00' * (padding_frames * fmt[0] * fmt[1]))
with wave.open(str(source / 'narration-de.wav'), 'wb') as writer:
    writer.setnchannels(format_info[0]); writer.setsampwidth(format_info[1]); writer.setframerate(format_info[2])
    for data in audio_data: writer.writeframes(data)

total = sum(scene['Seconds'] for scene in scenes)
concat = []; captions = []; chapters = [';FFMETADATA1', 'title=TransportOrders - Aluminiumextrusion', 'comment=Originale deutsche WPF-Oberfläche; isolierter Simulationslauf', 'language=deu']
elapsed = 0
for i, scene in enumerate(scenes):
    frame = Image.new('RGB', (1920, 1080), '#142337')
    draw = ImageDraw.Draw(frame)
    draw.rectangle((0, 0, 1920, 112), fill='#101c2d')
    draw.text((150, 18), 'TRANSPORTORDERS   /   ' + scene['Chapter'], font=bold(20), fill='#84d7d5')
    draw.text((150, 51), scene['Title'], font=bold(36), fill='#f5f8fc')
    draw.rounded_rectangle((1570, 25, 1770, 71), 10, fill='#24504d')
    draw.text((1592, 35), 'SIMULATION · DE', font=bold(18), fill='#cefff1')
    app = Image.open(source / scene['File']).convert('RGB')
    assert app.size == (1600, 820), 'Unexpected WPF capture size'
    frame.paste(app, (160, 122))
    draw.rectangle((159, 121, 1760, 942), outline='#5d7087', width=2)
    wrapped = lines(draw, scene['Caption'], regular(26), 1620)
    assert len(wrapped) <= 2, ('Caption too long', scene['Title'])
    for line_index, line in enumerate(wrapped):
        draw.text((150, 960 + 33 * line_index), line, font=regular(26), fill='#ecf2f8')
    draw.text((150, 1039), 'Original-WPF-Oberfläche · Simulierte Anlagen und SPS-Antworten', font=regular(16), fill='#a8bbcf')
    draw.text((1650, 1037), f'{i+1:02d} / {len(scenes):02d}', font=regular(18), fill='#a8bbcf')
    draw.rectangle((0, 1074, 1920, 1080), fill='#263a51')
    draw.rectangle((0, 1074, round(1920*(elapsed+scene['Seconds'])/total), 1080), fill='#55c6c4')
    name = f'video-{i+1:03d}.png'
    frame.save(source / name)
    concat.extend([f"file '{name}'", f"duration {scene['Seconds']:.6f}"])
    def stamp(t):
        milliseconds = round(t*1000); hours, rest = divmod(milliseconds, 3600000); minutes, rest = divmod(rest, 60000); seconds, ms = divmod(rest, 1000)
        return f'{hours:02d}:{minutes:02d}:{seconds:02d},{ms:03d}'
    captions.append(f"{i+1}\n{stamp(elapsed)} --> {stamp(elapsed+scene['Seconds'])}\n{scene['Title']}\n{scene['Caption']}\n")
    chapters.extend(['[CHAPTER]', 'TIMEBASE=1/1000', f'START={round(elapsed*1000)}', f"END={round((elapsed+scene['Seconds'])*1000)}", 'title='+scene['Title']])
    elapsed += scene['Seconds']
concat.append(f"file 'video-{len(scenes):03d}.png'")
(source/'video.ffconcat').write_text('\n'.join(concat)+'\n', encoding='utf-8')
(source/'chapters.txt').write_text('\n'.join(chapters)+'\n', encoding='utf-8')
(output/'TransportOrders-Demo-DE.srt').write_text('\n'.join(captions), encoding='utf-8')
destination = output/'TransportOrders-Demo-DE.mp4'
ffmpeg = shutil.which('ffmpeg')
if not ffmpeg: raise RuntimeError('ffmpeg is required')
subprocess.run([ffmpeg, '-y', '-hide_banner', '-loglevel', 'warning', '-f', 'concat', '-safe', '0', '-i', str(source/'video.ffconcat'),
    '-i', str(source/'narration-de.wav'), '-f', 'ffmetadata', '-i', str(source/'chapters.txt'), '-map', '0:v:0', '-map', '1:a:0',
    '-map_metadata', '2', '-map_chapters', '2', '-vf', 'tpad=stop_mode=clone:stop_duration=3', '-c:v', 'libx264', '-preset', 'medium', '-crf', '20', '-r', '25', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '128k', '-movflags', '+faststart', '-t', f'{total:.6f}', '-metadata:s:a:0', 'language=deu', '-metadata:s:v:0', 'language=deu', str(destination)], check=True)
(output/'demo-video-manifest.json').write_text(json.dumps({'duration_seconds': total, 'scenes': scenes, 'resolution': '1920x1080',
    'language': 'de-DE', 'narration': 'Microsoft Hedda Desktop', 'source': str(source), 'video': str(destination)}, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({'video': str(destination), 'seconds': total, 'scenes': len(scenes)}, ensure_ascii=False))
