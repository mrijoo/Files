﻿using Files.Filesystem;
using Files.Filesystem.StorageItems;
using Files.Helpers;
using Files.Services;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.DependencyInjection;
using Microsoft.Toolkit.Mvvm.Input;
using Microsoft.Toolkit.Uwp;
using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Xaml.Controls;

namespace Files.ViewModels.SettingsViewModels
{
    public class AboutViewModel : ObservableObject
    {
        private IUserSettingsService UserSettingsService { get; } = Ioc.Default.GetService<IUserSettingsService>();
        private IBundlesSettingsService BundlesSettingsService { get; } = Ioc.Default.GetService<IBundlesSettingsService>();
        protected IFileTagsSettingsService FileTagsSettingsService { get; } = Ioc.Default.GetService<IFileTagsSettingsService>();

        public RelayCommand OpenLogLocationCommand => new RelayCommand(() => SettingsViewModel.OpenLogLocation());
        public RelayCommand CopyVersionInfoCommand => new RelayCommand(() => CopyVersionInfo());

        public ICommand ExportSettingsCommand { get; }

        public ICommand ImportSettingsCommand { get;  }

        public RelayCommand<ItemClickEventArgs> ClickAboutFeedbackItemCommand =>
            new RelayCommand<ItemClickEventArgs>(ClickAboutFeedbackItem);

        public AboutViewModel()
        {
            ExportSettingsCommand = new AsyncRelayCommand(ExportSettings);
            ImportSettingsCommand = new AsyncRelayCommand(ImportSettings);
        }

        private async Task ExportSettings()
        {
            FileSavePicker filePicker = new FileSavePicker();
            filePicker.FileTypeChoices.Add("Zip File", new[] { ".zip" });
            filePicker.SuggestedFileName = $"Files_{App.AppVersion}";

            StorageFile file = await filePicker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    var zipFolder = await ZipStorageFolder.FromStorageFileAsync(file);
                    if (zipFolder == null)
                    {
                        return;
                    }
                    var localFolderPath = ApplicationData.Current.LocalFolder.Path;
                    // Export user settings
                    var userSettings = await zipFolder.CreateFileAsync(Constants.LocalSettings.UserSettingsFileName, CreationCollisionOption.ReplaceExisting);
                    string exportSettings = (string)UserSettingsService.ExportSettings();
                    await userSettings.WriteTextAsync(exportSettings);
                    // Export bundles
                    var bundles = await zipFolder.CreateFileAsync(Constants.LocalSettings.BundlesSettingsFileName, CreationCollisionOption.ReplaceExisting);
                    string exportBundles = (string)BundlesSettingsService.ExportSettings();
                    await bundles.WriteTextAsync(exportBundles);
                    // Export pinned items
                    var pinnedItems = await BaseStorageFile.GetFileFromPathAsync(Path.Combine(localFolderPath, Constants.LocalSettings.SettingsFolderName, App.SidebarPinnedController.JsonFileName));
                    await pinnedItems.CopyAsync(zipFolder, pinnedItems.Name, NameCollisionOption.ReplaceExisting);
                    // Export terminals config
                    var terminals = await BaseStorageFile.GetFileFromPathAsync(Path.Combine(localFolderPath, Constants.LocalSettings.SettingsFolderName, App.TerminalController.JsonFileName));
                    await terminals.CopyAsync(zipFolder, terminals.Name, NameCollisionOption.ReplaceExisting);
                    // Export file tags list and DB
                    var fileTagsList = await zipFolder.CreateFileAsync(Constants.LocalSettings.FileTagSettingsFileName, CreationCollisionOption.ReplaceExisting);
                    string exportTags = (string)FileTagsSettingsService.ExportSettings();
                    await fileTagsList.WriteTextAsync(exportTags);
                    var fileTagsDB = await zipFolder.CreateFileAsync(Path.GetFileName(FileTagsHelper.FileTagsDbPath), CreationCollisionOption.ReplaceExisting);
                    string exportTagsDB = FileTagsHelper.DbInstance.Export();
                    await fileTagsDB.WriteTextAsync(exportTagsDB);
                }
                catch (Exception ex)
                {
                    App.Logger.Warn(ex, "Error exporting settings");
                }
            }
        }

        private async Task ImportSettings()
        {
            FileOpenPicker filePicker = new FileOpenPicker();
            filePicker.FileTypeFilter.Add(".zip");

            StorageFile file = await filePicker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    var zipFolder = await ZipStorageFolder.FromStorageFileAsync(file);
                    if (zipFolder == null)
                    {
                        return;
                    }
                    var localFolderPath = ApplicationData.Current.LocalFolder.Path;
                    var settingsFolder = await StorageFolder.GetFolderFromPathAsync(Path.Combine(localFolderPath, Constants.LocalSettings.SettingsFolderName));
                    // Import user settings
                    var userSettingsFile = await zipFolder.GetFileAsync(Constants.LocalSettings.UserSettingsFileName);
                    string importSettings = await userSettingsFile.ReadTextAsync();
                    UserSettingsService.ImportSettings(importSettings);
                    // Import bundles
                    var bundles = await zipFolder.GetFileAsync(Constants.LocalSettings.BundlesSettingsFileName);
                    string importBundles = await bundles.ReadTextAsync();
                    BundlesSettingsService.ImportSettings(importBundles);
                    // Import pinned items
                    var pinnedItems = await zipFolder.GetFileAsync(App.SidebarPinnedController.JsonFileName);
                    await pinnedItems.CopyAsync(settingsFolder, pinnedItems.Name, NameCollisionOption.ReplaceExisting);
                    await App.SidebarPinnedController.ReloadAsync();
                    // Import terminals config
                    var terminals = await zipFolder.GetFileAsync(App.TerminalController.JsonFileName);
                    await terminals.CopyAsync(settingsFolder, terminals.Name, NameCollisionOption.ReplaceExisting);
                    // Import file tags list and DB
                    var fileTagsList = await zipFolder.GetFileAsync(Constants.LocalSettings.FileTagSettingsFileName);
                    string importTags = await fileTagsList.ReadTextAsync();
                    FileTagsSettingsService.ImportSettings(importTags);
                    var fileTagsDB = await zipFolder.GetFileAsync(Path.GetFileName(FileTagsHelper.FileTagsDbPath));
                    string importTagsDB = await fileTagsDB.ReadTextAsync();
                    FileTagsHelper.DbInstance.Import(importTagsDB);
                }
                catch (Exception ex)
                {
                    App.Logger.Warn(ex, "Error importing settings");
                    UIHelpers.CloseAllDialogs();
                    await DialogDisplayHelper.ShowDialogAsync("SettingsImportErrorTitle".GetLocalized(), "SettingsImportErrorDescription".GetLocalized());
                }
            }
        }

        public void CopyVersionInfo()
        {
            Common.Extensions.IgnoreExceptions(() =>
            {
                DataPackage dataPackage = new DataPackage();
                dataPackage.RequestedOperation = DataPackageOperation.Copy;
                dataPackage.SetText(Version + "\nOS Version: " + SystemInformation.Instance.OperatingSystemVersion);
                Clipboard.SetContent(dataPackage);
            });
        }

        public string Version
        {
            get
            {
                var version = Package.Current.Id.Version;
                return string.Format($"{"SettingsAboutVersionTitle".GetLocalized()} {version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
            }
        }

        public string AppName
        {
            get
            {
                return Package.Current.DisplayName;
            }
        }

        private async void ClickAboutFeedbackItem(ItemClickEventArgs e)
        {
            var clickedItem = (StackPanel)e.ClickedItem;
            switch (clickedItem.Tag)
            {
                case "Feedback":
                    SettingsViewModel.ReportIssueOnGitHub();
                    break;

                case "ReleaseNotes":
                    await Launcher.LaunchUriAsync(new Uri(@"https://github.com/files-community/Files/releases"));
                    break;

                case "Documentation":
                    await Launcher.LaunchUriAsync(new Uri(@"https://files.community/docs"));
                    break;

                case "Contributors":
                    await Launcher.LaunchUriAsync(new Uri(@"https://github.com/files-community/Files/graphs/contributors"));
                    break;

                case "PrivacyPolicy":
                    await Launcher.LaunchUriAsync(new Uri(@"https://github.com/files-community/Files/blob/main/Privacy.md"));
                    break;

                case "SupportUs":
                    await Launcher.LaunchUriAsync(new Uri(@"https://github.com/sponsors/yaichenbaum"));
                    break;

                default:
                    break;
            }
        }
    }
}
