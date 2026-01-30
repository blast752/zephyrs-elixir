namespace ZephyrsElixir.UI.Dialogs;

public sealed class DialogService
{
    #region Singleton

    private static readonly Lazy<DialogService> LazyInstance = new(() => new DialogService());
    public static DialogService Instance => LazyInstance.Value;

    private DialogService() { }

    #endregion

    #region Quick Methods - Info, Warning, Error, Success

    public void ShowInfo(string messageKey, Window? owner = null, string? titleKey = null)
    {
        var config = new DialogConfig
        {
            Title = GetString(titleKey ?? "Dialog_Title_Info"),
            Message = GetString(messageKey),
            Type = DialogType.Info,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };

        Show(config);
    }

    public void ShowWarning(string messageKey, Window? owner = null, string? titleKey = null)
    {
        var config = new DialogConfig
        {
            Title = GetString(titleKey ?? "Dialog_Title_Warning"),
            Message = GetString(messageKey),
            Type = DialogType.Warning,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };

        Show(config);
    }

    public void ShowError(string messageKey, Window? owner = null, string? titleKey = null)
    {
        var config = new DialogConfig
        {
            Title = GetString(titleKey ?? "Dialog_Title_Error"),
            Message = GetString(messageKey),
            Type = DialogType.Error,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };

        Show(config);
    }

    public void ShowSuccess(string messageKey, Window? owner = null, string? titleKey = null)
    {
        var config = new DialogConfig
        {
            Title = GetString(titleKey ?? "Dialog_Title_Success"),
            Message = GetString(messageKey),
            Type = DialogType.Success,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };

        Show(config);
    }

    #endregion

    #region Confirmation Dialogs

    public bool Confirm(string messageKey, Window? owner = null, string? titleKey = null)
    {
        var config = new DialogConfig
        {
            Title = GetString(titleKey ?? "Common_Confirm_Title"),
            Message = GetString(messageKey),
            Type = DialogType.Question,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString("Common_Button_No"), DialogAction.No, ButtonStyle.Secondary),
                new DialogButton(GetString("Common_Button_Yes"), DialogAction.Yes, ButtonStyle.Primary)
            }
        };

        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Yes;
    }

    public bool ConfirmCustom(string messageKey, string confirmButtonKey, string cancelButtonKey, 
                               Window? owner = null, string? titleKey = null)
    {
        var config = new DialogConfig
        {
            Title = GetString(titleKey ?? "Common_Confirm_Title"),
            Message = GetString(messageKey),
            Type = DialogType.Question,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString(cancelButtonKey), DialogAction.Cancel, ButtonStyle.Secondary),
                new DialogButton(GetString(confirmButtonKey), DialogAction.Primary, ButtonStyle.Primary)
            }
        };

        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Primary;
    }

    public bool ConfirmDirect(string message, Window? owner = null, string? title = null)
    {
        var config = new DialogConfig
        {
            Title = title ?? GetString("Common_Confirm_Title"),
            Message = message,
            Type = DialogType.Question,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString("Common_Button_Cancel"), DialogAction.Cancel, ButtonStyle.Secondary),
                new DialogButton(GetString("Common_Button_OK"), DialogAction.Primary, ButtonStyle.Primary)
            }
        };
        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Primary;
    }

    public bool ConfirmStopOptimization(Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = GetString("Dialog_StopOptimization_Title"),
            Message = GetString("Dialog_StopOptimization_Description"),
            Type = DialogType.Warning,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString("Dialog_StopOptimization_ContinueButton"), DialogAction.Cancel, ButtonStyle.Secondary),
                new DialogButton(GetString("Dialog_StopOptimization_StopButton"), DialogAction.Primary, ButtonStyle.Accent)
            }
        };

        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Primary;
    }

    #endregion

    #region Pro Required Dialogs

    public bool ShowProRequired(string featureMessageKey, Window? owner = null)
    {
        var message = $"{GetString(featureMessageKey)}\n\n{GetString("Pro_Required_Upgrade_Question")}";

        var config = new DialogConfig
        {
            Title = GetString("Pro_Required_Title"),
            Message = message,
            Type = DialogType.ProRequired,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString("Common_Button_No"), DialogAction.No, ButtonStyle.Secondary),
                new DialogButton(GetString("Dialog_Button_Upgrade"), DialogAction.Upgrade, ButtonStyle.Accent)
            }
        };

        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Upgrade;
    }

    public void ShowProRequiredWithUpgrade(string featureMessageKey, Window? owner = null)
    {
        var ownerWindow = owner ?? GetActiveWindow();
        
        if (ShowProRequired(featureMessageKey, ownerWindow))
        {
            var licenseDialog = new LicenseDialog { Owner = ownerWindow };
            licenseDialog.ShowDialog();
        }
    }

    #endregion

    #region Custom Dialog

    public DialogAction ShowCustom(DialogConfig config)
    {
        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result;
    }

    public void ShowInfoDirect(string title, string message, Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = title,
            Message = message,
            Type = DialogType.Info,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };

        Show(config);
    }

    public void ShowFormatted(DialogType type, string messageKey, Window? owner = null, params object[] args)
    {
        var message = string.Format(GetString(messageKey), args);
        
        var config = new DialogConfig
        {
            Title = GetTitleForType(type),
            Message = message,
            Type = type,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { OkButton }
        };

        Show(config);
    }

    #endregion

    #region Private Helpers

    private static void Show(DialogConfig config)
    {
        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
    }

    private static string GetString(string key) => 
        TranslationManager.Instance[key];

    private static Window? GetActiveWindow() =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ??
        Application.Current.MainWindow;

    private static string GetTitleForType(DialogType type) => type switch
    {
        DialogType.Info => GetString("Dialog_Title_Info"),
        DialogType.Success => GetString("Dialog_Title_Success"),
        DialogType.Warning => GetString("Dialog_Title_Warning"),
        DialogType.Error => GetString("Dialog_Title_Error"),
        DialogType.Question => GetString("Common_Confirm_Title"),
        DialogType.ProRequired => GetString("Pro_Required_Title"),
        _ => GetString("Dialog_Title_Info")
    };

    private static DialogButton OkButton => 
        new(GetString("Common_Button_OK"), DialogAction.Primary, ButtonStyle.Primary);

    #endregion

    #region Info Dialogs (License, Privacy, Changelog)

    public void ShowLicense(Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = GetString("Info_License_Title"),
            Type = DialogType.RichContent,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { new DialogButton(GetString("Dialog_Button_GotIt"), DialogAction.Primary, ButtonStyle.Primary) },
            RichContent = CreateLicenseContent()
        };

        Show(config);
    }

    public void ShowPrivacy(Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = GetString("Info_Privacy_Title"),
            Type = DialogType.RichContent,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { new DialogButton(GetString("Dialog_Button_GotIt"), DialogAction.Primary, ButtonStyle.Primary) },
            RichContent = CreatePrivacyContent()
        };

        Show(config);
    }

    public void ShowChangelog(Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = GetString("Info_Changelog_Title"),
            Type = DialogType.RichContent,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[] { new DialogButton(GetString("Dialog_Button_GotIt"), DialogAction.Primary, ButtonStyle.Primary) },
            RichContent = CreateChangelogContent()
        };

        Show(config);
    }

    #endregion

    #region Rich Content Factories

    private static IEnumerable<Block> CreateLicenseContent()
    {
        yield return CreateParagraph(GetString("Info_License_Copyright"));
        yield return CreateParagraph(GetString("Info_License_Permission"));
        yield return CreateParagraph(GetString("Info_License_Conditions"));
        yield return CreateParagraph(GetString("Info_License_Disclaimer"));
        yield return CreateParagraphWithLink(GetString("Info_License_MoreInfo"), "https://elixirsite.vercel.app/terms");
    }

    private static IEnumerable<Block> CreatePrivacyContent()
    {
        yield return CreateSectionTitle(GetString("Info_Privacy_Title"));

        var points = new[]
        {
            GetString("Info_Privacy_Point1"),
            GetString("Info_Privacy_Point2"),
            GetString("Info_Privacy_Point3"),
            GetString("Info_Privacy_Point4"),
            GetString("Info_Privacy_Point5")
        };

        var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(10, 0, 0, 10) };
        foreach (var point in points)
        {
            list.ListItems.Add(new ListItem(CreateParagraph(point)) { Margin = new Thickness(0, 2, 0, 2) });
        }
        yield return list;

        yield return CreateParagraphWithLink(GetString("Info_Privacy_MoreInfo"), "https://elixirsite.vercel.app/privacy");
    }

    private static IEnumerable<Block> CreateChangelogContent()
    {
        var sections = new (string TitleKey, string Icon, string[] ItemKeys)[]
        {
            ("Info_Changelog_New", "🆕", new[]
            {
                "Info_Changelog_New_1", "Info_Changelog_New_2", "Info_Changelog_New_3",
                "Info_Changelog_New_4", "Info_Changelog_New_5", "Info_Changelog_New_6",
                "Info_Changelog_New_7", "Info_Changelog_New_8", "Info_Changelog_New_9",
                "Info_Changelog_New_10", "Info_Changelog_New_11", "Info_Changelog_New_12"
            }),
            ("Info_Changelog_Updated", "🚀", new[]
            {
                "Info_Changelog_Updated_1", "Info_Changelog_Updated_2", "Info_Changelog_Updated_3",
                "Info_Changelog_Updated_4", "Info_Changelog_Updated_5", "Info_Changelog_Updated_6",
                "Info_Changelog_Updated_7", "Info_Changelog_Updated_8"
            }),
            ("Info_Changelog_Removed", "❌", new[]
            {
                "Info_Changelog_Removed_1"
            })
        };

        foreach (var (titleKey, icon, itemKeys) in sections)
        {
            yield return CreateChangelogSection($"{icon} {GetString(titleKey)}");

            var list = new List
            {
                MarkerStyle = TextMarkerStyle.Disc,
                Margin = new Thickness(15, 0, 0, 12),
                Foreground = TextBrush
            };

            foreach (var key in itemKeys)
            {
                list.ListItems.Add(new ListItem(new Paragraph(new Run(GetString(key)))
                {
                    Margin = new Thickness(0, 2, 0, 2),
                    FontSize = 13
                }));
            }

            yield return list;
        }
    }

    #endregion

    #region FlowDocument Helpers

    private static readonly Brush TextBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFDCDCF0")!);
    private static readonly Brush TitleBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFD700")!);
    private static readonly Brush LinkBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF87CEFA")!);

    static DialogService()
    {
        TextBrush.Freeze();
        TitleBrush.Freeze();
        LinkBrush.Freeze();
    }

    private static Paragraph CreateParagraph(string text) => new(new Run(text))
    {
        Foreground = TextBrush,
        FontSize = 14,
        Margin = new Thickness(0, 0, 0, 10),
        TextAlignment = TextAlignment.Left
    };

    private static Paragraph CreateSectionTitle(string text) => new(new Run(text))
    {
        Foreground = TitleBrush,
        FontSize = 16,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 12)
    };

    private static Paragraph CreateChangelogSection(string text) => new(new Run(text))
    {
        Foreground = TitleBrush,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 6)
    };

    private static Paragraph CreateParagraphWithLink(string text, string url)
    {
        var paragraph = new Paragraph { Foreground = TextBrush, FontSize = 14, Margin = new Thickness(0, 8, 0, 0) };
        paragraph.Inlines.Add(new Run(text));

        var hyperlink = new Hyperlink(new Run(url)) { NavigateUri = new Uri(url), Foreground = LinkBrush };
        hyperlink.RequestNavigate += (_, e) =>
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        };

        paragraph.Inlines.Add(hyperlink);
        return paragraph;
    }

    #endregion

    #region Update Dialog

    public bool ShowUpdate(UpdateInfo updateInfo, Window? owner = null)
    {
        var config = new DialogConfig
        {
            Title = GetString("Update_Title"),
            Type = DialogType.RichContent,
            Owner = owner ?? GetActiveWindow(),
            Buttons = new[]
            {
                new DialogButton(GetString("Update_Button_Skip"), DialogAction.Cancel, ButtonStyle.Secondary),
                new DialogButton(GetString("Update_Button_UpdateNow"), DialogAction.Primary, ButtonStyle.Primary)
            },
            RichContent = CreateUpdateContent(updateInfo)
        };

        var dialog = UnifiedDialog.Create(config);
        dialog.ShowDialog();
        return dialog.Result == DialogAction.Primary;
    }

    private static IEnumerable<Block> CreateUpdateContent(UpdateInfo updateInfo)
    {
        yield return CreateParagraph(string.Format(GetString("Update_NewVersion"), updateInfo.Version));
        yield return CreateSectionTitle(GetString("Update_ReleaseNotes"));
        yield return CreateParagraph(updateInfo.ReleaseNotes);
    }

    #endregion
}
