
using System;
using System.Linq;
using DroneFlasher.Helpers;
using DroneFlasher.ViewModels;
using Xunit;

namespace DroneFlasher.Tests.ViewModelsTests;

public class ThemeViewModelTests
{
    private static ThemeViewModel CreateViewModel(
        Action<string>? applyTheme = null,
        Action<string?, double?>? applyBackground = null,
        Func<string?>? openFileDialog = null,
        Action<string, string>? showError = null)
    {
        return new ThemeViewModel(
            applyTheme ?? (_ => { }),
            applyBackground ?? ((_, _) => { }),
            openFileDialog ?? (() => null),
            showError ?? ((_, _) => { }));
    }

    [Fact]
    public void Constructor_InitializesThemes()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal(12, vm.Themes.Count);
        Assert.Contains(vm.Themes, t => t.Key == "Dark");
        Assert.Contains(vm.Themes, t => t.Key == "Light");
        Assert.Equal(
            ThemeManager.CurrentBackgroundOpacity,
            vm.BackgroundOpacity);
    }

    [Fact]
    public void SelectThemeCommand_AppliesThemeAndMarksItSelected()
    {
        // Arrange
        string? appliedTheme = null;
        var vm = CreateViewModel(
            applyTheme: key => appliedTheme = key);

        // Act
        vm.SelectThemeCommand.Execute("Dark");

        // Assert
        Assert.Equal("Dark", appliedTheme);
        Assert.True(vm.Themes.Single(t => t.Key == "Dark").IsSelected);
        Assert.All(
            vm.Themes.Where(t => t.Key != "Dark"),
            theme => Assert.False(theme.IsSelected));
    }

    [Fact]
    public void SelectThemeCommand_WhenThemeKeyIsEmpty_DoesNothing()
    {
        // Arrange
        string? appliedTheme = null;
        var vm = CreateViewModel(
            applyTheme: key => appliedTheme = key);

        // Act
        vm.SelectThemeCommand.Execute(string.Empty);

        // Assert
        Assert.Null(appliedTheme);
    }

    [Fact]
    public void SelectThemeCommand_WhenApplyingFails_ShowsError()
    {
        // Arrange
        string? errorTitle = null;
        string? errorMessage = null;

        var vm = CreateViewModel(
            applyTheme: _ => throw new InvalidOperationException("Test error"),
            showError: (title, message) =>
            {
                errorTitle = title;
                errorMessage = message;
            });

        // Act
        vm.SelectThemeCommand.Execute("Dark");

        // Assert
        Assert.Equal("Помилка зміни теми", errorTitle);
        Assert.Contains("Test error", errorMessage);
    }

    [Fact]
    public void SelectImageCommand_WhenFileIsSelected_AppliesBackground()
    {
        // Arrange
        string? selectedPath = null;
        double? appliedOpacity = null;
        const string expectedPath = @"C:\test-background.jpg";

        var vm = CreateViewModel(
            applyBackground: (path, opacity) =>
            {
                selectedPath = path;
                appliedOpacity = opacity;
            },
            openFileDialog: () => expectedPath);

        // Act
        vm.SelectImageCommand.Execute(null);

        // Assert
        Assert.Equal(expectedPath, selectedPath);
        Assert.Equal(vm.BackgroundOpacity, appliedOpacity);
    }

    [Fact]
    public void SelectImageCommand_WhenSelectionIsCancelled_DoesNothing()
    {
        // Arrange
        bool backgroundApplied = false;

        var vm = CreateViewModel(
            applyBackground: (_, _) => backgroundApplied = true,
            openFileDialog: () => null);

        // Act
        vm.SelectImageCommand.Execute(null);

        // Assert
        Assert.False(backgroundApplied);
    }

    [Fact]
    public void ClearImageCommand_ClearsBackground()
    {
        // Arrange
        bool actionCalled = false;
        string? appliedPath = "not cleared";
        double? appliedOpacity = 0.5;

        var vm = CreateViewModel(
            applyBackground: (path, opacity) =>
            {
                actionCalled = true;
                appliedPath = path;
                appliedOpacity = opacity;
            });

        // Act
        vm.ClearImageCommand.Execute(null);

        // Assert
        Assert.True(actionCalled);
        Assert.Null(appliedPath);
        Assert.Null(appliedOpacity);
    }

    [Fact]
    public void BackgroundOpacity_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange
        var vm = CreateViewModel();
        string? changedProperty = null;

        vm.PropertyChanged += (_, e) =>
            changedProperty = e.PropertyName;

        // Act
        vm.BackgroundOpacity = 0.5;

        // Assert
        Assert.Equal(0.5, vm.BackgroundOpacity);
        Assert.Equal(nameof(vm.BackgroundOpacity), changedProperty);
    }
}
