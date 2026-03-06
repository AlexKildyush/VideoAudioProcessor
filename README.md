# VideoAudioProcessor

Desktop WPF application for media file processing and project rendering (video collage/slideshow) using FFmpeg.

## What this project does
- Loads media files into a local queue.
- Processes media files (trim, transcode, optional custom FFmpeg command).
- Stores processed output files.
- Creates editable media projects and renders final videos.

## Prerequisites

### Required software
1. Windows 10/11 (WPF desktop app).
2. .NET SDK 8.0+ (project target: `net8.0-windows`).
3. FFmpeg (must include both `ffmpeg` and `ffprobe` executables).

### Verify prerequisites
Run in PowerShell:

```powershell
dotnet --version
ffmpeg -version
ffprobe -version
```

If `ffmpeg`/`ffprobe` commands are not found, add FFmpeg `bin` folder to your `PATH` and restart terminal/IDE.

## Project setup (first run)
1. Clone repository:
   ```powershell
   git clone <repo-url>
   cd VideoAudioProcessor
   ```
2. Restore dependencies:
   ```powershell
   dotnet restore
   ```
3. Build:
   ```powershell
   dotnet build VideoAudioProcessor.sln
   ```
4. Run:
   ```powershell
   dotnet run --project .\VideoAudioProcessor\VideoAudioProcessor.csproj
   ```

## Local data structure
The app asks for a root folder on first file upload and uses it as workspace.

Inside selected root folder it creates/uses:

```text
TrackManager/
  Queue/                  # source media queue
  Processed/              # processed output files
  Projects/
    VideoCollage/         # project JSON files
    SlideShow/            # project JSON files
```

## Services and components
This repository is a single desktop app (no microservices, no database).

- `UI (WPF)`: main user workflow (upload, queue, processing, projects).
- `FFmpeg integration`: executes media processing and rendering commands.
- `FFprobe integration`: reads media metadata (duration/audio stream checks).
- `File system storage`: persists queue/processed files and project JSON state.

## Usage documentation

### In-app documentation
- Open the app and go to **"О приложении"** (About).
- This is the default screen at startup and contains user instructions.

### Developer-oriented code entry points
- `VideoAudioProcessor/MainWindow.xaml` - UI layout and screens.
- `VideoAudioProcessor/MainWindow.xaml.cs` - app-level navigation and base actions.
- `VideoAudioProcessor/MainWindow.Process.xaml.cs` - single file processing via FFmpeg.
- `VideoAudioProcessor/MainWindow.ProjectRender.xaml.cs` - project rendering and FFprobe logic.
- `VideoAudioProcessor/MainWindow.Projects.xaml.cs` - project editor/list logic.
