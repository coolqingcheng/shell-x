using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging.Abstractions;
using ShellX.Desktop.Features.FileBrowser;
using ShellX.Desktop.Features.Transfers;
using ShellX.Infrastructure.Files;

namespace ShellX.Desktop.Tests;

public sealed class TransferTests
{
    [AvaloniaFact]
    public void FinishedTransferIgnoresDelayedProgress()
    {
        foreach (var result in new[] { "已完成", "已取消", "失败" })
        {
            using var transfer = new TransferViewModel("qy.exe", "↑ 上传", 1024);
            transfer.Report(256);
            switch (result)
            {
                case "已完成": transfer.Complete(); break;
                case "已取消": transfer.FinishCanceled(uploading: true); break;
                default: transfer.Fail("权限不足"); break;
            }
            var detail = transfer.DetailText;
            var percent = transfer.Percent;
            transfer.Report(512);
            Assert.Equal(result, transfer.Status);
            Assert.Equal(percent, transfer.Percent);
            Assert.Equal(detail, transfer.DetailText);
            Assert.False(transfer.CancelCommand.CanExecute(null));
        }
    }

    [AvaloniaFact]
    public void CancellationStopsProgressUpdatesAndDisablesRepeatedCancellation()
    {
        using var transfer = new TransferViewModel("qy.exe", "↑ 上传", 1024);
        transfer.Report(256); var progress = transfer.Percent;
        transfer.CancelCommand.Execute(null); transfer.Report(512);
        Assert.True(transfer.Token.IsCancellationRequested);
        Assert.Equal("取消中", transfer.Status); Assert.Equal(progress, transfer.Percent);
        Assert.False(transfer.CancelCommand.CanExecute(null));
        transfer.CancelCommand.Execute(null); transfer.FinishCanceled(uploading: true);
        Assert.Equal("已取消", transfer.Status); Assert.Equal("远端可能保留部分文件", transfer.DetailText);
    }

    [AvaloniaFact]
    public void EmptyTransferCompletesWithoutInvalidPercentage()
    {
        using var transfer = new TransferViewModel("empty.txt", "↑ 上传", 0);
        transfer.Report(0); Assert.Equal(0, transfer.Percent);
        transfer.Complete(); transfer.Report(0);
        Assert.Equal(100, transfer.Percent); Assert.Equal("已完成", transfer.Status);
    }

    [AvaloniaFact]
    public async Task TransferTextAndProgressHaveSeparateVisibleBounds()
    {
        foreach (var width in new[] { 480d, 1050d })
        {
            using var vm = new FileBrowserViewModel(null!, NullLogger<FileBrowserViewModel>.Instance, new LocalFileSystem()) { ShowTransfers = true };
            var transfer = new TransferViewModel("较长的上传文件名称-qy.exe", "↑ 上传", 1024);
            transfer.Report(256); transfer.FinishCanceled(uploading: true);
            var active = new TransferViewModel("正在上传的另一个文件.exe", "↑ 上传", 1024);
            active.Report(512);
            vm.Transfers.Add(transfer);
            vm.Transfers.Add(active);
            var view = new FileBrowserView { DataContext = vm };
            var window = new Window { Width = width, Height = 360, Content = view };
            window.Show();
            try
            {
                await Task.Delay(30);
                var controls = view.GetVisualDescendants().OfType<Control>().ToArray();
                var inactiveProgress = controls.OfType<ProgressBar>().Single(x => ReferenceEquals(x.DataContext, transfer));
                var inactiveCancel = controls.OfType<Button>().Single(x => ReferenceEquals(x.DataContext, transfer) && Equals(x.Content, "取消"));
                Assert.False(inactiveProgress.IsVisible); Assert.False(inactiveCancel.IsVisible);
                var progress = controls.OfType<ProgressBar>().Single(x => ReferenceEquals(x.DataContext, active));
                var progressBounds = InView(progress, view);
                Assert.True(progress.Bounds.Width > 0); Assert.True(progress.IsVisible);
                Assert.True(progressBounds.Left >= 0 && progressBounds.Right <= view.Bounds.Width);
                foreach (var text in controls.OfType<TextBlock>().Where(x => ReferenceEquals(x.DataContext, active)))
                {
                    var textBounds = InView(text, view);
                    Assert.False(textBounds.Intersects(progressBounds), $"{width}px: text={textBounds}; progress={progressBounds}");
                }
                var details = controls.OfType<TextBlock>().Single(x => x.Text == transfer.DetailText);
                Assert.True(InView(details, view).Right <= view.Bounds.Width);
            }
            finally { window.Close(); }
        }
    }

    private static Rect InView(Control control, Control view)
        => control.Bounds.WithX(0).WithY(0).TransformToAABB(control.TransformToVisual(view)!.Value);
}
