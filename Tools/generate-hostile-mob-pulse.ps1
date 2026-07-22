Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$sampleRate = 44100
$durationSeconds = 0.120
$sampleCount = [int][Math]::Round($sampleRate * $durationSeconds)
# Keep this synchronized with AuthoredAudioLevels.NormalizedOneShotPeak.
$targetPeak = 0.70
# Preserve the perceived energy of the previous levelized triangle pulse when
# changing waveform. Peak-only normalization makes square waves sound louder.
$targetRms = 0.183
$fundamentalHertz = 520.0
$rawSamples = [double[]]::new($sampleCount)
$noiseState = [uint64]0x5EED1234
$maximumAbsoluteSample = 0.0
$sumRawSquares = 0.0

for ($index = 0; $index -lt $sampleCount; $index++) {
    $time = $index / [double]$sampleRate
    $phase = 2.0 * [Math]::PI * $fundamentalHertz * $time
    $attack = 1.0 - [Math]::Exp(-$time / 0.0035)
    $release = [Math]::Exp(-$time / 0.032)
    $tailStart = 0.095
    $tail = if ($time -le $tailStart) {
        1.0
    }
    else {
        $tailProgress = [Math]::Min(1.0, ($time - $tailStart) / ($durationSeconds - $tailStart))
        $tailAngle = $tailProgress * [Math]::PI * 0.5
        [Math]::Cos($tailAngle) * [Math]::Cos($tailAngle)
    }

    $noiseState = ([uint64]1664525 * $noiseState + [uint64]1013904223) -band [uint64]4294967295
    $noise = ([double]$noiseState / 2147483647.5) - 1.0
    # A band-limited square wave using the first five odd harmonics keeps the
    # urgent square timbre without folding high harmonics back into the signal.
    $square =
        [Math]::Sin($phase) +
        [Math]::Sin(3.0 * $phase) / 3.0 +
        [Math]::Sin(5.0 * $phase) / 5.0 +
        [Math]::Sin(7.0 * $phase) / 7.0 +
        [Math]::Sin(9.0 * $phase) / 9.0
    $transient = 0.035 * $noise * [Math]::Exp(-$time / 0.008)
    $sample = $attack * $tail * ($release * $square + $transient)
    $rawSamples[$index] = $sample
    $maximumAbsoluteSample = [Math]::Max($maximumAbsoluteSample, [Math]::Abs($sample))
    $sumRawSquares += $sample * $sample
}

if ($maximumAbsoluteSample -le 0.0) {
    throw "Pulse synthesis produced silence."
}

$rawRms = [Math]::Sqrt($sumRawSquares / $sampleCount)
$normalizationGain = [Math]::Min($targetPeak / $maximumAbsoluteSample, $targetRms / $rawRms)
$pcmSamples = [int16[]]::new($sampleCount)
$pcmPeak = 0
for ($index = 0; $index -lt $sampleCount; $index++) {
    $normalized = $rawSamples[$index] * $normalizationGain
    $encoded = [int][Math]::Round(
        [Math]::Max(-1.0, [Math]::Min(1.0, $normalized)) * 32767.0,
        [MidpointRounding]::AwayFromZero)
    $pcmSamples[$index] = [int16]$encoded
    $pcmPeak = [Math]::Max($pcmPeak, [Math]::Abs($encoded))
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$outputPath = Join-Path $repositoryRoot "Mods\Terrarium\Assets\Audio\HostileMobPulse.wav"
$outputDirectory = Split-Path -Parent $outputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$dataLength = $pcmSamples.Length * 2
$fileStream = [IO.File]::Open($outputPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$writer = [IO.BinaryWriter]::new($fileStream)
try {
    $writer.Write([Text.Encoding]::ASCII.GetBytes("RIFF"))
    $writer.Write([uint32](36 + $dataLength))
    $writer.Write([Text.Encoding]::ASCII.GetBytes("WAVE"))
    $writer.Write([Text.Encoding]::ASCII.GetBytes("fmt "))
    $writer.Write([uint32]16)
    $writer.Write([uint16]1)
    $writer.Write([uint16]1)
    $writer.Write([uint32]$sampleRate)
    $writer.Write([uint32]($sampleRate * 2))
    $writer.Write([uint16]2)
    $writer.Write([uint16]16)
    $writer.Write([Text.Encoding]::ASCII.GetBytes("data"))
    $writer.Write([uint32]$dataLength)
    foreach ($sample in $pcmSamples) {
        $writer.Write($sample)
    }
}
finally {
    $writer.Dispose()
    $fileStream.Dispose()
}

Write-Host "Generated $outputPath"
Write-Host ("Format: mono 16-bit PCM, {0} Hz, {1:N1} ms, peak {2:P1}" -f $sampleRate, ($sampleCount * 1000.0 / $sampleRate), ($pcmPeak / 32767.0))
