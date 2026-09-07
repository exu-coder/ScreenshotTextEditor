# Screenshot Text Editor

**Offline-first native Windows application** for detecting, editing and replacing text inside screenshots and images.

All processing (OCR, inpainting, font matching, rendering) runs **100% locally**.  
No cloud APIs. No Python. No Node.js. No internet required.

## Features

- Import PNG / JPG / WEBP / BMP / TIFF
- Drag & drop and Ctrl+V paste
- Fully local OCR (Tesseract)
- Automatic text region detection with clickable overlays
- Click any detected text → replace it
- Background reconstruction via OpenCV inpainting (Telea / Navier-Stokes)
- Best-effort font matching against installed Windows fonts
- Style-aware text rendering (SkiaSharp)
- Layers panel
- Undo / Redo
- Export high-quality PNG / JPEG
- Native Windows print dialog + print preview
- Dark modern UI

## Requirements for development

- Windows 10 / 11
- .NET 8 SDK
- Visual Studio 2022 or `dotnet` CLI

## OCR data (required)

Download English trained data and place it here:

```
src/ScreenshotTextEditor/Resources/tessdata/eng.traineddata
```

Get it from:  
https://github.com/tesseract-ocr/tessdata/raw/main/eng.traineddata

## Build & Run

```bash
dotnet restore
dotnet build -c Release
dotnet run --project src/ScreenshotTextEditor
```

## Publish self-contained EXE

```bash
dotnet publish src/ScreenshotTextEditor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The output will be a single `ScreenshotTextEditor.exe` (plus native dependencies extracted at runtime).

## GitHub Actions

Push a tag `v*` or run the **Build Windows EXE** workflow manually.  
It produces a self-contained Windows x64 artifact and creates a GitHub Release.

## Architecture

```
ScreenshotTextEditor/
├── OCR/               Local Tesseract engine
├── ImageProcessing/   OpenCV inpainting
├── FontMatching/      Installed-font heuristics
├── Rendering/         SkiaSharp text compositing
├── Editing/           Undo / Redo history
├── Models/            TextRegion, ProjectDocument
└── UI                 WPF MainWindow + canvas
```

## Privacy

- Images never leave the machine
- No telemetry by default
- Works in Airplane mode

## Limitations (honest)

Perfect font reconstruction from pure raster pixels is not possible.  
The app uses the strongest practical local approximations (size, weight, color, installed font matching, horizontal scaling) and exposes manual overrides.

## License

MIT
