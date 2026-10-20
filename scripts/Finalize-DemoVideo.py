"""Hold the last frame through the narration and validate the exported streams."""
from pathlib import Path
import argparse, json, shutil, subprocess
parser = argparse.ArgumentParser()
parser.add_argument('frames', type=Path)
args = parser.parse_args()
source = args.frames.resolve()
root = Path(__file__).resolve().parent.parent
movie = root/'output'/'video'/'TransportOrders-Demo-DE.mp4'
manifest = json.loads((movie.parent/'demo-video-manifest.json').read_text(encoding='utf-8'))
total = manifest['duration_seconds']
ffmpeg = shutil.which('ffmpeg'); ffprobe = shutil.which('ffprobe')
def run(command):
    subprocess.run([ffmpeg, '-y', '-hide_banner', '-loglevel', 'error', *command], check=True)
def probe():
    return json.loads(subprocess.check_output([ffprobe, '-v', 'error', '-show_streams', '-show_format', '-show_chapters', '-of', 'json', str(movie)]))
report = probe()
video = next(s for s in report['streams'] if s['codec_type']=='video')
if float(video['duration']) < total - 0.08:
    # A variable-duration image concat can stop duplicating before the final audio packet.
    # Encode only the short missing tail and copy the existing encoded picture and sound.
    prefix = source/'encoded-prefix.mp4'; tail = source/'encoded-tail.mp4'
    run(['-i', str(movie), '-map', '0:v:0', '-an', '-c:v', 'copy', str(prefix)])
    last = source / f"video-{len(manifest['scenes']):03d}.png"
    run(['-loop', '1', '-i', str(last), '-t', str(total-float(video['duration'])+0.12), '-c:v', 'libx264', '-preset', 'medium',
         '-crf', '20', '-r', '25', '-pix_fmt', 'yuv420p', str(tail)])
    listing = source/'encoded-final.ffconcat'
    listing.write_text("file 'encoded-prefix.mp4'\nfile 'encoded-tail.mp4'\n", encoding='utf-8')
    repaired = source/'finalized-demo.mp4'
    run(['-f','concat','-safe','0','-i',str(listing),'-i',str(movie),'-map','0:v:0','-map','1:a:0','-c','copy',
         '-map_metadata','1','-map_chapters','1','-t',str(total),'-movflags','+faststart',
         '-metadata:s:v:0','language=deu','-metadata:s:a:0','language=deu',str(repaired)])
    shutil.move(str(repaired), str(movie))
report = probe()
video = next(s for s in report['streams'] if s['codec_type']=='video')
audio = next(s for s in report['streams'] if s['codec_type']=='audio')
assert (video['width'], video['height']) == (1920,1080)
assert video['codec_name']=='h264' and audio['codec_name']=='aac'
assert abs(float(video['duration'])-float(audio['duration']))<0.1
assert audio['tags']['language']=='deu' and len(report['chapters'])==24
run(['-i', str(movie), '-f', 'null', '-'])
(root/'artifacts'/'demo-video-validation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({'file': str(movie), 'seconds': report['format']['duration'], 'chapters': len(report['chapters']), 'decoded': True}))
