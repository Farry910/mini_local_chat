# Downloads Google's Noto Animated Emoji (CC BY 4.0) used as the app's built-in emoji stickers,
# then shrinks them to 128 px with ShrinkGifs.cs into src\MiniLocalChat\Assets\Emoji.
# Run from the repo root:  powershell -ExecutionPolicy Bypass -File tools\fetch-emoji.ps1
# The list must match Services\EmojiStickers.cs.
$emoji = [ordered]@{
    "grinning" = "1f600"; "joy" = "1f602"; "rofl" = "1f923"; "heart-eyes" = "1f60d"
    "kiss" = "1f618"; "cool" = "1f60e"; "thinking" = "1f914"; "sob" = "1f62d"
    "angry" = "1f621"; "scream" = "1f631"; "party" = "1f973"; "sleeping" = "1f634"
    "mind-blown" = "1f92f"; "eye-roll" = "1f644"; "halo" = "1f607"; "hearts" = "1f970"
    "thumbs-up" = "1f44d"; "clap" = "1f44f"; "pray" = "1f64f"; "wave" = "1f44b"
    "heart" = "2764_fe0f"; "fire" = "1f525"; "tada" = "1f389"; "hundred" = "1f4af"
}

$work = Join-Path $env:TEMP "noto-emoji-download"
New-Item -ItemType Directory -Force $work | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
foreach ($name in $emoji.Keys) {
    $target = Join-Path $work "$name.gif"
    if (Test-Path $target) { continue }
    $url = "https://fonts.gstatic.com/s/e/notoemoji/latest/$($emoji[$name])/512.gif"
    try {
        Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $target -TimeoutSec 60
        "downloaded $name"
    } catch {
        "FAILED $name ($url): $($_.Exception.Message)"
    }
}

dotnet run tools/ShrinkGifs.cs -- $work "src\MiniLocalChat\Assets\Emoji" 128
