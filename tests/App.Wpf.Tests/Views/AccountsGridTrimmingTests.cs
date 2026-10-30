namespace RbxManager.App.Tests.Views;

using System.Linq;
using System.Windows;
using System.Windows.Controls;
using NSubstitute;
using RbxManager.App.Tests.Helpers;
using RbxManager.App.ViewModels;
using RbxManager.App.Views.Pages;
using RbxManager.Core.Auth;
using RbxManager.Core.Backup;
using RbxManager.Core.Launch;
using RbxManager.Core.Settings;
using RbxManager.Core.Storage;
using Wpf.Ui;
using Xunit;

/// <summary>M44 Flow 7: free-text cells (Alias, Group, Username, Last checked) must ellipsize, never hard-clip.</summary>
public sealed class AccountsGridTrimmingTests
{
    [Fact]
    public void AccountsDataGrid_FreeTextColumns_UseCharacterEllipsisWithTooltip()
    {
        StaRunner.Run(() =>
        {
            TestAppResources.Initialize("Dark");
            ISettingsService settingsService = Substitute.For<ISettingsService>();
            settingsService.CurrentSettings.Returns(AppSettings.Default);
            AccountsViewModel viewModel = new(
                Substitute.For<IAccountStore>(),
                Substitute.For<IContentDialogService>(),
                Substitute.For<ISnackbarService>(),
                Substitute.For<IRobloxAuthService>(),
                Substitute.For<ILauncher>(),
                settingsService,
                Substitute.For<IBackupService>());

            AccountsPage page = new(viewModel);
            DataGridTextColumn[] textColumns = page.AccountsDataGrid.Columns.OfType<DataGridTextColumn>().ToArray();
            Assert.True(textColumns.Length >= 3);

            foreach (DataGridTextColumn column in textColumns.Where(c => c.SortMemberPath is "Alias" or "Group" or "Username"))
            {
                Style? style = column.ElementStyle;
                Assert.NotNull(style);
                Setter? trimming = style!.Setters.OfType<Setter>().FirstOrDefault(s => s.Property == TextBlock.TextTrimmingProperty);
                Assert.Equal(TextTrimming.CharacterEllipsis, trimming?.Value);
                Assert.Contains(style.Setters.OfType<Setter>(), s => s.Property == FrameworkElement.ToolTipProperty);
                Assert.Contains(
                    style.Triggers.OfType<Trigger>(),
                    t => t.Property == TextBlock.TextProperty && Equals(t.Value, string.Empty) &&
                         t.Setters.OfType<Setter>().Any(s => s.Property == FrameworkElement.ToolTipProperty && s.Value == null));
            }
        });
    }
}
