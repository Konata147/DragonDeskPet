$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$modelRoot = Join-Path $projectRoot 'assets\ocr'
New-Item -ItemType Directory -Path $modelRoot -Force | Out-Null
$models = @(
    @{ Name = 'ch_PP-OCRv5_rec_mobile.onnx'; Url = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx'; Hash = '5825FC7EBF84AE7A412BE049820B4D86D77620F204A041697B0494669B1742C5' },
    @{ Name = 'ppocrv5_dict.txt'; Url = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/ppocrv5_dict.txt'; Hash = 'D1979E9F794C464C0D2E0B70A7FE14DD978E9DC644C0E71F14158CDF8342AF1B' }
)
foreach ($model in $models) {
    $target = Join-Path $modelRoot $model.Name
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $model.Hash) { continue }
    $staging = $target + '.download'
    Invoke-WebRequest -UseBasicParsing -Uri $model.Url -OutFile $staging -TimeoutSec 120
    if ((Get-FileHash -LiteralPath $staging).Hash -ne $model.Hash) { throw "OCR model hash mismatch: $($model.Name)" }
    Move-Item -LiteralPath $staging -Destination $target -Force
}
Write-Host 'Chinese OCR models verified. Runtime recognition is offline.'
