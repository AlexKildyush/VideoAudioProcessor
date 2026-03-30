using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VideoAudioProcessor.Services;

public sealed class TrackStorageService(string rootPath)
{
    public string RootPath { get; } = rootPath;
    public string TrackManagerPath => Path.Combine(RootPath, "TrackManager");
    public string QueuePath => Path.Combine(TrackManagerPath, "Queue");
    public string ProcessedPath => Path.Combine(TrackManagerPath, "Processed");
    public string ProjectsRootPath => Path.Combine(TrackManagerPath, "Projects");

    public string GetProjectsPath(ProjectType type)
    {
        var folder = type == ProjectType.VideoCollage ? "VideoCollage" : "SlideShow";
        return Path.Combine(ProjectsRootPath, folder);
    }

    public string GetProjectFilePath(ProjectType type, string name)
    {
        return Path.Combine(GetProjectsPath(type), $"{name}.json");
    }

    public string GetProcessedOutputPath(string fileName, string format)
    {
        return Path.Combine(ProcessedPath, $"{fileName}.{format}");
    }

    public void EnsureQueueDirectory()
    {
        Directory.CreateDirectory(QueuePath);
    }

    public void EnsureProcessedDirectory()
    {
        Directory.CreateDirectory(ProcessedPath);
    }

    public void EnsureProjectsDirectory(ProjectType type)
    {
        Directory.CreateDirectory(GetProjectsPath(type));
    }

    public string GetUniqueQueueFilePath(string originalFileName)
    {
        EnsureQueueDirectory();
        return Path.Combine(QueuePath, GetUniqueQueueFileName(originalFileName));
    }

    public string GetUniqueQueueFileName(string originalFileName)
    {
        EnsureQueueDirectory();

        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);
        var candidateName = originalFileName;
        var version = 1;

        while (File.Exists(Path.Combine(QueuePath, candidateName)))
        {
            candidateName = $"{baseName}({version}){extension}";
            version++;
        }

        return candidateName;
    }

    public IEnumerable<string> GetSupportedQueueFiles()
    {
        return GetSupportedFiles(QueuePath);
    }

    public IEnumerable<string> GetSupportedProcessedFiles()
    {
        return GetSupportedFiles(ProcessedPath);
    }

    public List<string> ListProjectNames(ProjectType type)
    {
        EnsureProjectsDirectory(type);
        return Directory.GetFiles(GetProjectsPath(type), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != null)
            .Cast<string>()
            .OrderBy(name => name)
            .ToList();
    }

    public void SaveProject(ProjectData project)
    {
        var projectPath = GetProjectFilePath(project.Type, project.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        var json = JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(projectPath, json);
    }

    public ProjectData LoadProject(ProjectType type, string name)
    {
        var projectPath = GetProjectFilePath(type, name);
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("Файл проекта не найден.", projectPath);
        }

        var project = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(projectPath));
        return project ?? throw new InvalidOperationException("Не удалось загрузить проект.");
    }

    public void DeleteProject(ProjectType type, string name)
    {
        var projectPath = GetProjectFilePath(type, name);
        if (File.Exists(projectPath))
        {
            File.Delete(projectPath);
        }
    }

    public void RenameProject(ProjectType type, string oldName, string newName)
    {
        var oldProjectPath = GetProjectFilePath(type, oldName);
        var newProjectPath = GetProjectFilePath(type, newName);
        if (File.Exists(newProjectPath))
        {
            throw new InvalidOperationException("Проект с таким названием уже существует.");
        }

        if (File.Exists(oldProjectPath))
        {
            File.Move(oldProjectPath, newProjectPath);
        }
    }

    public string CopyToQueue(string sourcePath)
    {
        EnsureQueueDirectory();
        var destinationName = GetUniqueQueueFileName(Path.GetFileName(sourcePath));
        var destinationPath = Path.Combine(QueuePath, destinationName);
        File.Copy(sourcePath, destinationPath);
        return destinationPath;
    }

    public void DeleteQueueFile(string fileName)
    {
        var path = Path.Combine(QueuePath, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteProcessedFile(string fileName)
    {
        var path = Path.Combine(ProcessedPath, fileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public List<TrackStorageItem> GetBaseTracks(Func<string, bool> predicate)
    {
        var results = new List<TrackStorageItem>();
        if (Directory.Exists(QueuePath))
        {
            results.AddRange(Directory.GetFiles(QueuePath)
                .Where(predicate)
                .Select(path => new TrackStorageItem(path, "В очереди")));
        }

        if (Directory.Exists(ProcessedPath))
        {
            results.AddRange(Directory.GetFiles(ProcessedPath)
                .Where(predicate)
                .Select(path => new TrackStorageItem(path, "Обработанные")));
        }

        return results.OrderBy(item => item.Source).ThenBy(item => item.FileName).ToList();
    }

    private static IEnumerable<string> GetSupportedFiles(string path)
    {
        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory.GetFiles(path)
            .Where(f => MediaFormats.Supported.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Select(Path.GetFileName)
            .Where(name => name != null)
            .Cast<string>()
            .ToList();
    }
}

public sealed class TrackStorageItem(string path, string source)
{
    public string Path { get; } = path;
    public string Source { get; } = source;
    public string FileName => System.IO.Path.GetFileName(Path);
    public string DisplayName => $"[{Source}] {FileName}";
}
