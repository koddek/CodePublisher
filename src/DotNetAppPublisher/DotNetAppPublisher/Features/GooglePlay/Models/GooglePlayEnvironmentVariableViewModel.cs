using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DotNetAppPublisher.Features.GooglePlay.Setup;

namespace DotNetAppPublisher.Features.GooglePlay.Models;

public sealed partial class GooglePlayEnvironmentVariableViewModel : ObservableObject
{
    private readonly EnvironmentVariableService _service;
    private readonly Action _changed;

    [ObservableProperty]
    private string value = string.Empty;

    [ObservableProperty]
    private string status = "Not configured";

    [ObservableProperty]
    private bool isSet;

    [ObservableProperty]
    private bool hasError;

    internal GooglePlayEnvironmentVariableViewModel(
        GooglePlayEnvironmentVariableDefinition definition,
        EnvironmentVariableService service,
        Action changed)
    {
        Name = definition.Name;
        DisplayName = definition.DisplayName;
        Description = definition.Description;
        Example = definition.Example;
        SetupInstructions = definition.SetupInstructions;
        HelpUrl = definition.HelpUrl;
        IsRequired = definition.IsRequired;
        IsSecret = definition.IsSecret;
        _service = service;
        _changed = changed;
        Refresh();
    }

    public string Name { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public string Example { get; }
    public string SetupInstructions { get; }
    public string HelpUrl { get; }
    public bool IsRequired { get; }
    public bool IsSecret { get; }
    public bool CanPersist => _service.SupportsPersistentUserVariables;
    public bool CanOpenHelp => !OperatingSystem.IsBrowser();
    public bool HasHelpUrl => !string.IsNullOrWhiteSpace(HelpUrl);
    public string ScopeLabel => CanPersist ? "User profile" : "This app session";
    public string DetectedValueLabel => IsSet
        ? IsSecret ? "Detected · hidden" : $"Detected · {Value}"
        : "Not detected";
    public bool IsRequiredAndMissing => IsRequired && !IsSet;

    public void Refresh()
    {
        var state = _service.GetState(Name);
        Value = state.Value ?? string.Empty;
        Status = state.Status;
        IsSet = state.IsSet;
        HasError = false;
    }

    [RelayCommand]
    private void SetForSession()
    {
        try
        {
            _service.SetForSession(Name, Value);
            Refresh();
            _changed();
            Status = "Configured for this app session";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private void SaveForUser()
    {
        try
        {
            _service.SetForUser(Name, Value);
            _service.SetForSession(Name, Value);
            Refresh();
            _changed();
            Status = _service.SupportsPersistentUserVariables
                ? "Saved for future launches"
                : "Saved for this app session";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private void Remove()
    {
        try
        {
            _service.RemoveForSession(Name);
            if (CanPersist)
            {
                _service.RemoveForUser(Name);
            }

            Refresh();
            _changed();
            Status = "Not configured";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    [RelayCommand]
    private void OpenSetupSource()
    {
        if (HasHelpUrl)
        {
            EnvironmentVariableService.OpenUrl(HelpUrl);
        }
    }

    private void ShowError(Exception exception)
    {
        HasError = true;
        Status = exception.Message;
    }
}

public sealed record GooglePlayEnvironmentVariableDefinition(
    string Name,
    string Description,
    bool IsRequired,
    bool IsSecret,
    string DisplayName = "",
    string Example = "",
    string SetupInstructions = "",
    string HelpUrl = "");
