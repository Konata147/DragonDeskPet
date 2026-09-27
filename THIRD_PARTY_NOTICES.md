# Third-party notices

DragonDeskPet uses the following third-party packages:

- **Ical.Net 5.2.3** — MIT License — https://github.com/ical-org/ical.net

The package is used only to parse standard iCalendar (`.ics`) course exports. Its license and authorship remain with the Ical.Net project contributors.

## V0.4 course import

- ExcelDataReader 3.9.0 — MIT — https://github.com/ExcelDataReader/ExcelDataReader
- System.Text.Encoding.CodePages 8.0.0 — MIT — https://github.com/dotnet/runtime
- RapidOcrNet 4.2.0 — Apache-2.0 — https://github.com/BobLd/RapidOcrNet
- PaddleOCR PP-OCRv5 models and dictionary — Apache-2.0 — https://github.com/PaddlePaddle/PaddleOCR
- RapidAI ONNX model conversion/distribution v3.9.2 — Apache-2.0 — https://github.com/RapidAI/RapidOCR
- Microsoft.ML.OnnxRuntime / Managed 1.29.0 — MIT — https://github.com/microsoft/onnxruntime
- SkiaSharp and native assets 3.119.1 — MIT (includes Skia third-party notices) — https://github.com/mono/SkiaSharp
- Clipper2 2.0.0 — Boost Software License 1.0 — https://github.com/AngusJohnson/Clipper2
- Microsoft.Web.WebView2 1.0.3650.58 — Microsoft proprietary SDK license — https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3650.58/License

Model provenance, pinned checksums and the Apache-2.0 license text are included
under `assets/ocr/`. Models perform local inference; importing course images does
not call a cloud AI provider. WebView2 Runtime is distributed separately by Microsoft.
