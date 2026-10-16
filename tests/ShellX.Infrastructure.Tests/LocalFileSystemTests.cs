using ShellX.Infrastructure.Files;
using ShellX.Core.Files;

namespace ShellX.Infrastructure.Tests;

public sealed class LocalFileSystemTests
{
    [Fact]
    public async Task LocalTextEditingPreservesUtf8AndRejectsConflictsAndOversizedContent()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "shellx-edit-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "config.txt"); var files = new LocalFileSystem(root);
        try
        {
            await File.WriteAllBytesAsync(path, [0xef, 0xbb, 0xbf, (byte)'a', 13, 10]);
            var original = await files.ReadTextAsync(path);
            Assert.Equal("a\r\n", original.Content); Assert.True(original.Encoding.HasBom);
            var saved = await files.SaveTextAsync(original, original.Content + "中文\r\n");
            saved = await files.SaveTextAsync(saved, saved.Content + "next\r\n");
            Assert.Equal(saved.Content, (await files.ReadTextAsync(path)).Content);
            Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, (await File.ReadAllBytesAsync(path))[..3]);
            await Assert.ThrowsAsync<IOException>(() => files.SaveTextAsync(saved, new string('a', TextEditorLimits.MaxLineLength + 1)));
            Assert.Equal(saved.Content, (await files.ReadTextAsync(path)).Content);
            await File.WriteAllTextAsync(path, saved.Content.Replace("next", "xxxx"), System.Text.Encoding.UTF8);
            File.SetLastWriteTimeUtc(path, saved.ModifiedUtc); // 同大小、同时间的外部改动仍必须检出。
            await Assert.ThrowsAsync<IOException>(() => files.SaveTextAsync(saved, "must not overwrite"));
            Assert.Contains("xxxx", await File.ReadAllTextAsync(path));
            using (var large = File.Create(path)) large.SetLength(TextEditorLimits.MaxBytes + 1L);
            await Assert.ThrowsAsync<IOException>(() => files.ReadTextAsync(path));
            foreach (var content in new[] { new string('a', TextEditorLimits.MaxLineLength + 1), new string('\n', TextEditorLimits.MaxLines), new string('x', TextEditorLimits.MaxCharacters + 1), "binary\0data" })
            {
                await File.WriteAllTextAsync(path, content);
                await Assert.ThrowsAsync<IOException>(() => files.ReadTextAsync(path));
            }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.ReadTextAsync(path, cancel.Token));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task DirectoryUploadPlanIncludesNestedEmptyAndHiddenEntriesAndSupportsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-upload-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "中文目录", "empty"));
        await File.WriteAllTextAsync(Path.Combine(root, ".hidden"), "hidden");
        await File.WriteAllTextAsync(Path.Combine(root, "中文目录", "a.txt"), "中文\n");
        try
        {
            var files = new LocalFileSystem();
            var plan = await files.PlanDirectoryUploadAsync(root);
            Assert.Equal(Path.GetFileName(root), plan.Name);
            Assert.Contains("", plan.Directories); Assert.Contains("中文目录/empty", plan.Directories);
            Assert.Contains(plan.Files, f => f.RelativePath == ".hidden");
            Assert.Contains(plan.Files, f => f.RelativePath == "中文目录/a.txt");
            Assert.Equal(13, plan.TotalBytes); Assert.Equal(0, plan.SkippedLinks);
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.PlanDirectoryUploadAsync(root, cancel.Token));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task LocalChangesRejectPathsAndNeverOverwriteExistingFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "shellx-local-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var files = new LocalFileSystem(root);
            await files.CreateAsync(root, "folder", true);
            await files.CreateAsync(root, "first.txt", false);
            await files.CreateAsync(root, "second.txt", false);
            await File.WriteAllTextAsync(Path.Combine(root, "first.txt"), "keep");
            await Assert.ThrowsAsync<IOException>(() => files.CreateAsync(root, "first.txt", false));
            await Assert.ThrowsAsync<IOException>(() => files.RenameAsync(Path.Combine(root, "second.txt"), "first.txt"));
            await Assert.ThrowsAsync<ArgumentException>(() => files.CreateAsync(root, "../escape.txt", false));
            await Assert.ThrowsAsync<ArgumentException>(() => files.RenameAsync(Path.Combine(root, "second.txt"), "..\\escape.txt"));
            Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(root, "first.txt")));
            var listed = await files.ListAsync(root);
            Assert.Equal(Path.GetFullPath(root), listed.Path);
            Assert.True(listed.Entries.Single(e => e.Name == "folder").IsDirectory);
            Assert.Equal(4, listed.Entries.Single(e => e.Name == "first.txt").Size);
        }
        finally { Directory.Delete(root, true); }
    }
}
