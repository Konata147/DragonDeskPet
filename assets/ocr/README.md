# Offline Chinese OCR models

Runtime: RapidOcrNet 4.2.0 (Apache-2.0), using ONNX Runtime CPU.
Chinese recognizer/dictionary: PaddleOCR PP-OCRv5, redistributed by RapidAI
at version v3.9.2 under Apache-2.0. Source manifest:
https://github.com/RapidAI/RapidOCR/blob/main/python/rapidocr/default_models.yaml

Run `scripts/get-ocr-models.ps1` before building a distributable package.
The script pins URLs and verifies SHA-256. Models are included in the release,
not downloaded when users import an image. No image is sent to these servers.
Detector and orientation models ship with the pinned RapidOcrNet package.
The extra Latin recognizer shipped by that package is not used for course OCR.

Chinese recognizer SHA256:
5825FC7EBF84AE7A412BE049820B4D86D77620F204A041697B0494669B1742C5

Dictionary SHA256:
D1979E9F794C464C0D2E0B70A7FE14DD978E9DC644C0E71F14158CDF8342AF1B
