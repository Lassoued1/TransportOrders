[CmdletBinding()]
param([Parameter(Mandatory)][string]$ScenesPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $voice = $speaker.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'de-DE' } | Select-Object -First 1
    if (-not $voice) { throw 'A German Windows speech voice is required.' }
    $speaker.SelectVoice($voice.VoiceInfo.Name)
    $speaker.Rate = 0
    $root = Split-Path -Parent (Resolve-Path -LiteralPath $ScenesPath).Path
    $scenes = Get-Content -LiteralPath $ScenesPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($scene in $scenes) {
        $output = Join-Path $root ([IO.Path]::GetFileNameWithoutExtension($scene.File) + '.wav')
        $speaker.SetOutputToWaveFile($output)
        $speaker.Speak($scene.Title + '. ' + $scene.Caption)
        $speaker.SetOutputToNull()
    }
    Write-Output ('Narration voice: ' + $voice.VoiceInfo.Name)
} finally { $speaker.Dispose() }
